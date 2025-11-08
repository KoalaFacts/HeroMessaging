namespace HeroMessaging.ArchitectureTests;

/// <summary>
/// Tests to ensure messages (Commands, Queries, Events) are immutable.
/// Immutability is critical for thread-safety and predictable behavior.
/// </summary>
public class ImmutabilityTests
{
    private static readonly Assembly AbstractionsAssembly = typeof(Abstractions.IMessage).Assembly;
    private static readonly Assembly CoreAssembly = typeof(HeroMessagingService).Assembly;

    [Fact]
    [Trait("Category", "Architecture")]
    public void Commands_ShouldNotHavePublicSetters()
    {
        // Arrange
        var commands = Types.InAssemblies(new[] { AbstractionsAssembly, CoreAssembly })
            .That().ImplementInterface(typeof(Abstractions.Commands.ICommand))
            .Or().Inherit(typeof(Abstractions.Commands.ICommand<>))
            .GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface)
            .ToList();

        // Act & Assert
        foreach (var command in commands)
        {
            var mutableProperties = command.GetProperties()
                .Where(p => p.CanWrite && p.SetMethod?.IsPublic == true)
                .Where(p => p.SetMethod?.ReturnParameter?.GetRequiredCustomModifiers()
                    .All(m => m.Name != "IsExternalInit") == true) // Exclude init-only setters
                .ToList();

            Assert.Empty(mutableProperties);
        }
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void Events_ShouldNotHavePublicSetters()
    {
        // Arrange
        var events = Types.InAssemblies(new[] { AbstractionsAssembly, CoreAssembly })
            .That().ImplementInterface(typeof(Abstractions.Events.IEvent))
            .GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface)
            .ToList();

        // Act & Assert
        foreach (var @event in events)
        {
            var mutableProperties = @event.GetProperties()
                .Where(p => p.CanWrite && p.SetMethod?.IsPublic == true)
                .Where(p => p.SetMethod?.ReturnParameter?.GetRequiredCustomModifiers()
                    .All(m => m.Name != "IsExternalInit") == true) // Exclude init-only setters
                .ToList();

            Assert.Empty(mutableProperties);
        }
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void Queries_ShouldNotHavePublicSetters()
    {
        // Arrange
        var queries = Types.InAssemblies(new[] { AbstractionsAssembly, CoreAssembly })
            .That().Inherit(typeof(Abstractions.Queries.IQuery<>))
            .GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface)
            .ToList();

        // Act & Assert
        foreach (var query in queries)
        {
            var mutableProperties = query.GetProperties()
                .Where(p => p.CanWrite && p.SetMethod?.IsPublic == true)
                .Where(p => p.SetMethod?.ReturnParameter?.GetRequiredCustomModifiers()
                    .All(m => m.Name != "IsExternalInit") == true) // Exclude init-only setters
                .ToList();

            Assert.Empty(mutableProperties);
        }
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void Messages_ShouldBeSealed_OrAbstract()
    {
        // Arrange & Act
        var result = Types.InAssemblies(new[] { AbstractionsAssembly, CoreAssembly })
            .That().ImplementInterface(typeof(Abstractions.IMessage))
            .And().AreClasses()
            .Should().BeSealed()
            .Or().BeAbstract()
            .GetResult();

        // Assert - Allow some flexibility for base classes
        if (!result.IsSuccessful)
        {
            var violations = result.FailingTypeNames?
                .Where(t => !t.Contains("Base") && !t.Contains("Test"))
                .ToList() ?? new List<string>();

            Assert.Empty(violations);
        }
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void ValueObjects_ShouldBeRecords_OrStructs()
    {
        // This test checks that value objects use records or structs for value semantics

        // Arrange
        var valueObjectTypes = Types.InAssemblies(new[] { AbstractionsAssembly, CoreAssembly })
            .That().ResideInNamespace("HeroMessaging.Abstractions")
            .And().AreNotAbstract()
            .And().AreNotInterfaces()
            .GetTypes()
            .Where(t => t.Name.Contains("Options") ||
                       t.Name.Contains("Result") ||
                       t.Name.Contains("Context") ||
                       t.Name.Contains("Address") ||
                       t.Name.Contains("Envelope"))
            .ToList();

        // Act & Assert
        foreach (var type in valueObjectTypes)
        {
            var isRecord = type.GetMethod("<Clone>$") != null; // Records have compiler-generated Clone method
            var isStruct = type.IsValueType;
            var isImmutableClass = !type.IsValueType &&
                                   type.GetProperties().All(p => !p.CanWrite ||
                                                                p.SetMethod?.ReturnParameter?.GetRequiredCustomModifiers()
                                                                    .Any(m => m.Name == "IsExternalInit") == true);

            Assert.True(isRecord || isStruct || isImmutableClass,
                $"{type.FullName} should be a record, struct, or immutable class");
        }
    }
}
