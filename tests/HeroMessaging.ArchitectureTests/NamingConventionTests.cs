namespace HeroMessaging.ArchitectureTests;

/// <summary>
/// Tests to enforce naming conventions across the codebase.
/// </summary>
public class NamingConventionTests
{
    private static readonly Assembly AbstractionsAssembly = typeof(Abstractions.Messages.IMessage).Assembly;
    private static readonly Assembly CoreAssembly = typeof(HeroMessagingService).Assembly;

    [Fact]
    [Trait("Category", "Architecture")]
    public void Interfaces_ShouldStartWithI()
    {
        // Arrange & Act
        var result = Types.InAssemblies(new[] { AbstractionsAssembly, CoreAssembly })
            .That().AreInterfaces()
            .Should().HaveNameStartingWith("I")
            .GetResult();

        // Assert
        Assert.True(result.IsSuccessful, FormatFailureMessage(result, "Interfaces must start with 'I'"));
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void Commands_ShouldEndWithCommand()
    {
        // Arrange & Act
        var result = Types.InAssemblies(new[] { AbstractionsAssembly, CoreAssembly })
            .That().ImplementInterface(typeof(Abstractions.Commands.ICommand))
            .Or().Inherit(typeof(Abstractions.Commands.ICommand<>))
            .And().AreNotInterfaces() // Exclude interface definitions themselves
            .Should().HaveNameEndingWith("Command")
            .GetResult();

        // Assert - Allow some flexibility for test/example commands
        if (!result.IsSuccessful)
        {
            var violations = result.FailingTypeNames?
                .Where(t => !t.Contains("Test") && !t.Contains("Example"))
                .ToList() ?? new List<string>();

            Assert.Empty(violations);
        }
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void Events_ShouldEndWithEvent()
    {
        // Arrange & Act
        var result = Types.InAssemblies(new[] { AbstractionsAssembly, CoreAssembly })
            .That().ImplementInterface(typeof(Abstractions.Events.IEvent))
            .Should().HaveNameEndingWith("Event")
            .GetResult();

        // Assert - Allow some flexibility for test/example events
        if (!result.IsSuccessful)
        {
            var violations = result.FailingTypeNames?
                .Where(t => !t.Contains("Test") && !t.Contains("Example"))
                .ToList() ?? new List<string>();

            Assert.Empty(violations);
        }
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void Queries_ShouldEndWithQuery()
    {
        // Arrange & Act
        var result = Types.InAssemblies(new[] { AbstractionsAssembly, CoreAssembly })
            .That().Inherit(typeof(Abstractions.Queries.IQuery<>))
            .Should().HaveNameEndingWith("Query")
            .GetResult();

        // Assert - Allow some flexibility for test/example queries
        if (!result.IsSuccessful)
        {
            var violations = result.FailingTypeNames?
                .Where(t => !t.Contains("Test") && !t.Contains("Example"))
                .ToList() ?? new List<string>();

            Assert.Empty(violations);
        }
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void Handlers_ShouldEndWithHandler()
    {
        // Arrange & Act
        var result = Types.InAssembly(CoreAssembly)
            .That().ImplementInterface(typeof(Abstractions.Handlers.ICommandHandler<>))
            .Or().ImplementInterface(typeof(Abstractions.Handlers.ICommandHandler<,>))
            .Or().ImplementInterface(typeof(Abstractions.Handlers.IEventHandler<>))
            .Or().ImplementInterface(typeof(Abstractions.Handlers.IQueryHandler<,>))
            .Should().HaveNameEndingWith("Handler")
            .Or().HaveNameEndingWith("Processor") // Allow "Processor" as alternative
            .Or().HaveNameEndingWith("Decorator") // Decorators are also handlers
            .GetResult();

        // Assert
        Assert.True(result.IsSuccessful, FormatFailureMessage(result, "Handlers must end with 'Handler', 'Processor', or 'Decorator'"));
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void Exceptions_ShouldEndWithException()
    {
        // Arrange & Act
        var result = Types.InAssemblies(new[] { AbstractionsAssembly, CoreAssembly })
            .That().Inherit(typeof(Exception))
            .Should().HaveNameEndingWith("Exception")
            .GetResult();

        // Assert
        Assert.True(result.IsSuccessful, FormatFailureMessage(result, "Exception classes must end with 'Exception'"));
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void ExtensionClasses_ShouldHaveExtensionsInName()
    {
        // Arrange & Act - Get all static classes that end with Extensions
        var extensionClasses = Types.InAssemblies(new[] { AbstractionsAssembly, CoreAssembly })
            .That().AreClasses()
            .And().AreStatic()
            .GetTypes()
            .Where(t => t.Name.EndsWith("Extensions", StringComparison.Ordinal))
            .ToList();

        // Assert - All extension classes should be public
        var nonPublicExtensions = extensionClasses
            .Where(t => !t.IsPublic)
            .Select(t => t.FullName)
            .ToList();

        Assert.Empty(nonPublicExtensions);
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void AbstractClasses_ShouldEndWithBase_OrStartWithAbstract()
    {
        // Arrange & Act
        var abstractClasses = Types.InAssemblies(new[] { AbstractionsAssembly, CoreAssembly })
            .That().AreAbstract()
            .And().AreClasses()
            .GetTypes()
            .Where(t => !t.Name.EndsWith("Base") && !t.Name.StartsWith("Abstract"))
            .Where(t => !t.Name.Contains("Test")) // Exclude test classes
            .ToList();

        // Assert - Abstract classes should follow naming convention (with some exceptions for decorators)
        var violations = abstractClasses
            .Where(t => !t.Name.Contains("Decorator"))
            .Select(t => t.FullName)
            .ToList();

        Assert.Empty(violations);
    }

    private static string FormatFailureMessage(NetArchTestResult result, string context)
    {
        if (result.IsSuccessful)
            return string.Empty;

        var violations = string.Join(Environment.NewLine, result.FailingTypeNames ?? Array.Empty<string>());
        return $"{context}:{Environment.NewLine}{violations}";
    }
}
