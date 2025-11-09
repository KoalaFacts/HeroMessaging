namespace HeroMessaging.Tests.Architecture;

/// <summary>
/// Tests to enforce handler design principles.
/// Handlers should be stateless and use dependency injection.
/// </summary>
public class HandlerDesignTests
{
    private static readonly Assembly CoreAssembly = typeof(HeroMessagingService).Assembly;

    [Fact]
    [Trait("Category", "Architecture")]
    public void Handlers_ShouldNotHavePublicFields()
    {
        // Arrange
        var handlers = GetHandlerTypes();

        // Act & Assert
        foreach (var handler in handlers)
        {
            var publicFields = handler.GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => !f.IsInitOnly) // Allow readonly fields
                .ToList();

            Assert.Empty(publicFields);
        }
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void Handlers_ShouldNotHaveMutableState()
    {
        // Handlers should be stateless - all fields should be readonly

        // Arrange
        var handlers = GetHandlerTypes();

        // Act & Assert
        foreach (var handler in handlers)
        {
            var mutableFields = handler.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
                .Where(f => !f.IsInitOnly && !f.IsLiteral) // Non-readonly, non-const
                .Where(f => !f.Name.StartsWith('<')) // Exclude compiler-generated
                .ToList();

            Assert.Empty(mutableFields);
        }
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void Handlers_ShouldNotImplementIDisposable()
    {
        // Handlers are registered as transient/scoped in DI
        // They should not own disposable resources

        // Arrange & Act
        var result = Types.InAssembly(CoreAssembly)
            .That().ImplementInterface(typeof(Abstractions.Handlers.ICommandHandler<>))
            .Or().ImplementInterface(typeof(Abstractions.Handlers.ICommandHandler<,>))
            .Or().ImplementInterface(typeof(Abstractions.Handlers.IEventHandler<>))
            .Or().ImplementInterface(typeof(Abstractions.Handlers.IQueryHandler<,>))
            .ShouldNot().ImplementInterface(typeof(IDisposable))
            .GetResult();

        // Assert - Decorators may implement IDisposable
        if (!result.IsSuccessful)
        {
            var violations = result.FailingTypeNames?
                .Where(t => !t.Contains("Decorator"))
                .ToList() ?? new List<string>();

            Assert.Empty(violations);
        }
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void Handlers_ShouldHaveConstructor_WithDependencyInjection()
    {
        // Handlers should use constructor injection, not property injection

        // Arrange
        var handlers = GetHandlerTypes()
            .Where(h => !h.IsAbstract)
            .ToList();

        // Act & Assert
        foreach (var handler in handlers)
        {
            var constructors = handler.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

            // Should have at least one public constructor
            Assert.NotEmpty(constructors);

            // Check that there are no public property setters (property injection)
            var publicSetters = handler.GetProperties()
                .Where(p => p.CanWrite && p.SetMethod?.IsPublic == true)
                .ToList();

            // Allow init-only setters (for primary constructors)
            var mutablePublicProperties = publicSetters
                .Where(p => p.SetMethod?.ReturnParameter?.GetRequiredCustomModifiers()
                    .All(m => m.Name != "IsExternalInit") == true)
                .ToList();

            Assert.Empty(mutablePublicProperties);
        }
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void Decorators_ShouldHaveDecoratorInName()
    {
        // Decorator pattern is widely used - ensure naming consistency

        // Arrange
        var coreAssembly = CoreAssembly;

        // Act - Find classes that likely implement decorator pattern
        var decorators = coreAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .Where(t => ImplementsHandlerInterface(t))
            .Where(t => HasInnerHandlerField(t))
            .Where(t => !t.Name.Contains("Decorator"))
            .ToList();

        // Assert
        Assert.Empty(decorators);
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void Handlers_ShouldBeInternal_OrPublic()
    {
        // Handlers can be internal (same assembly) or public (cross-assembly)
        // They should not be private nested classes

        // Arrange & Act
        var result = Types.InAssembly(CoreAssembly)
            .That().ImplementInterface(typeof(Abstractions.Handlers.ICommandHandler<>))
            .Or().ImplementInterface(typeof(Abstractions.Handlers.ICommandHandler<,>))
            .Or().ImplementInterface(typeof(Abstractions.Handlers.IEventHandler<>))
            .Or().ImplementInterface(typeof(Abstractions.Handlers.IQueryHandler<,>))
            .Should().BePublic()
            .Or().NotBeNested() // Internal top-level classes are OK
            .GetResult();

        // Assert
        Assert.True(result.IsSuccessful, FormatFailureMessage(result));
    }

    private static List<Type> GetHandlerTypes()
    {
        return CoreAssembly.GetTypes()
            .Where(t => t.IsClass)
            .Where(t => ImplementsHandlerInterface(t))
            .ToList();
    }

    private static bool ImplementsHandlerInterface(Type type)
    {
        return type.GetInterfaces().Any(i =>
            (i.IsGenericType && (
                i.GetGenericTypeDefinition() == typeof(Abstractions.Handlers.ICommandHandler<>) ||
                i.GetGenericTypeDefinition() == typeof(Abstractions.Handlers.ICommandHandler<,>) ||
                i.GetGenericTypeDefinition() == typeof(Abstractions.Handlers.IEventHandler<>) ||
                i.GetGenericTypeDefinition() == typeof(Abstractions.Handlers.IQueryHandler<,>)
            ))
        );
    }

    private static bool HasInnerHandlerField(Type type)
    {
        var fields = type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
        return fields.Any(f => f.Name.Contains("inner", StringComparison.OrdinalIgnoreCase) ||
                              f.Name.Contains("handler", StringComparison.OrdinalIgnoreCase) ||
                              f.Name.Contains("next", StringComparison.OrdinalIgnoreCase));
    }

    private static string FormatFailureMessage(NetArchTestResult result)
    {
        if (result.IsSuccessful)
            return string.Empty;

        var violations = string.Join(Environment.NewLine, result.FailingTypeNames ?? Array.Empty<string>());
        return $"Handler design violation:{Environment.NewLine}{violations}";
    }
}
