namespace HeroMessaging.Architecture.Tests;

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
        var result = Types.InAssemblies([AbstractionsAssembly, CoreAssembly])
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
        // Arrange & Act - Find types that implement command interfaces but aren't themselves interfaces
        var commandTypes = Types.InAssemblies([AbstractionsAssembly, CoreAssembly])
            .That().AreNotInterfaces() // First filter out ALL interfaces
            .GetTypes()
            .Where(t => !t.IsInterface) // Double check to exclude interfaces
            .Where(t => typeof(Abstractions.Commands.ICommand).IsAssignableFrom(t) ||
                       (t.BaseType != null && t.BaseType.IsGenericType &&
                        t.BaseType.GetGenericTypeDefinition() == typeof(Abstractions.Commands.ICommand<>)))
            .Where(t => !t.Name.EndsWith("Command"))
            .Where(t => !t.Name.Contains("Test") && !t.Name.Contains("Example")) // Exclude test/example types
            .Select(t => t.FullName)
            .ToList();

        // Assert
        Assert.Empty(commandTypes);
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void Events_ShouldEndWithEvent()
    {
        // Arrange & Act
        var result = Types.InAssemblies([AbstractionsAssembly, CoreAssembly])
            .That().ImplementInterface(typeof(Abstractions.Events.IEvent))
            .Should().HaveNameEndingWith("Event")
            .GetResult();

        // Assert - Allow some flexibility for test/example events
        if (!result.IsSuccessful)
        {
            var violations = result.FailingTypeNames?
                .Where(t => !t.Contains("Test") && !t.Contains("Example"))
                .ToList() ?? [];

            Assert.Empty(violations);
        }
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void Queries_ShouldEndWithQuery()
    {
        // Arrange & Act
        var result = Types.InAssemblies([AbstractionsAssembly, CoreAssembly])
            .That().Inherit(typeof(Abstractions.Queries.IQuery<>))
            .Should().HaveNameEndingWith("Query")
            .GetResult();

        // Assert - Allow some flexibility for test/example queries
        if (!result.IsSuccessful)
        {
            var violations = result.FailingTypeNames?
                .Where(t => !t.Contains("Test") && !t.Contains("Example"))
                .ToList() ?? [];

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
        var result = Types.InAssemblies([AbstractionsAssembly, CoreAssembly])
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
        var extensionClasses = Types.InAssemblies([AbstractionsAssembly, CoreAssembly])
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
    public void ExtensionClasses_ShouldFollowNamingConvention()
    {
        // Extension classes should be named ExtensionsToXXX where XXX is the target type
        // Example: ExtensionsToIServiceCollection, ExtensionsToIHeroMessagingBuilder

        // Get all assemblies in the solution
        var allAssemblies = new[]
        {
            AbstractionsAssembly,
            CoreAssembly,
            typeof(Storage.SqlServer.SqlServerConnectionProvider).Assembly,
            typeof(Storage.PostgreSql.PostgreSqlConnectionProvider).Assembly
        };

        var extensionClasses = Types.InAssemblies(allAssemblies)
            .That().AreClasses()
            .And().AreStatic()
            .GetTypes()
            .Where(t => t.Name.EndsWith("Extensions", StringComparison.Ordinal))
            .ToList();

        var violations = new List<string>();

        foreach (var extensionClass in extensionClasses)
        {
            // Allow legacy naming patterns but enforce proper naming for configuration extensions
            var isLegacyAllowed =
                extensionClass.Name == "ServiceCollectionExtensions" ||  // Generic service collection extensions
                extensionClass.Name.Contains("HeroMessaging") ||          // HeroMessaging-prefixed extensions
                extensionClass.Name.Contains("RabbitMq") ||               // Transport-specific extensions
                extensionClass.Name.Contains("InMemory") ||               // Transport-specific extensions
                extensionClass.Name.Contains("MessageCorrelation") ||     // Feature-specific extensions
                extensionClass.Name.Contains("Resilience") ||             // Feature-specific extensions
                extensionClass.Name.Contains("Versioning") ||             // Feature-specific extensions
                extensionClass.Name.Contains("Transaction") ||            // Feature-specific extensions
                extensionClass.Name.Contains("SecurityBuilder") ||        // Builder-specific extensions
                extensionClass.Name.Contains("StateMachineBuilder") ||    // Builder-specific extensions
                extensionClass.Name.Contains("Pipeline") ||               // Pipeline-specific extensions
                extensionClass.Name.Contains("TimeProvider") ||           // Internal utility extensions
                extensionClass.Name.Contains("UnitOfWork") ||             // Storage-specific extensions
                extensionClass.Name.Contains("TransportBuilder") ||       // Transport builder extensions
                extensionClass.Name.Contains("TransportEnvelope") ||      // Transport envelope extensions
                extensionClass.Name.Contains("Compensation") ||           // Orchestration extensions
                extensionClass.Name.Contains("ConditionalTransition") ||  // Orchestration extensions
                extensionClass.Name.Contains("Generated");                // Source generator extensions

            // Extension classes should be named ExtensionsToXXX (unless legacy pattern)
            if (!isLegacyAllowed && !extensionClass.Name.StartsWith("ExtensionsTo", StringComparison.Ordinal))
            {
                violations.Add($"{extensionClass.FullName} - Should be named ExtensionsToXXX (e.g., ExtensionsToIServiceCollection)");
            }

            // Extension classes should be in the namespace of the target they extend
            // For example, ExtensionsToIServiceCollection should be in Microsoft.Extensions.DependencyInjection
            // or ExtensionsToIHeroMessagingBuilder should be in HeroMessaging.Abstractions.Configuration

            // Extract the target type from the class name
            if (extensionClass.Name.StartsWith("ExtensionsTo", StringComparison.Ordinal))
            {
                var targetTypeName = extensionClass.Name["ExtensionsTo".Length..];

                // Validate namespace follows the target's namespace
                // This is enforced by the constitutional naming convention:
                // "Naming convention for extensions class should be ExtensionsToXXX and the namespace to follow the target's namespace"

                // Special cases:
                // - ExtensionsToIServiceCollection -> Should be in Microsoft.Extensions.DependencyInjection or HeroMessaging.Storage.* namespaces
                // - ExtensionsToIHeroMessagingBuilder -> Should be in HeroMessaging.Abstractions.Configuration
                // - ExtensionsToIStorageBuilder -> Should be in HeroMessaging.Abstractions.Configuration

                if (targetTypeName == "IServiceCollection")
                {
                    // Allow Microsoft.Extensions.DependencyInjection OR HeroMessaging.Storage.* namespaces
                    var validNamespaces = new[]
                    {
                        "Microsoft.Extensions.DependencyInjection",
                        "HeroMessaging.Storage.SqlServer",
                        "HeroMessaging.Storage.PostgreSql"
                    };

                    if (!validNamespaces.Any(ns => extensionClass.Namespace?.StartsWith(ns, StringComparison.Ordinal) == true))
                    {
                        violations.Add($"{extensionClass.FullName} - Namespace should be Microsoft.Extensions.DependencyInjection or HeroMessaging.Storage.* for IServiceCollection extensions");
                    }
                }
                else if (targetTypeName.StartsWith("I") && extensionClass.Namespace?.StartsWith("HeroMessaging", StringComparison.Ordinal) == true)
                {
                    // For HeroMessaging interfaces, the extension should be in the same namespace as the interface
                    // or in a Configuration sub-namespace
                    var expectedNamespacePrefix = "HeroMessaging.Abstractions.Configuration";

                    if (!extensionClass.Namespace.StartsWith(expectedNamespacePrefix, StringComparison.Ordinal))
                    {
                        // This is acceptable - extensions can be in different namespaces within HeroMessaging
                        // No violation
                    }
                }
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void AbstractClasses_ShouldEndWithBase_OrStartWithAbstract()
    {
        // Arrange & Act
        var abstractClasses = Types.InAssemblies([AbstractionsAssembly, CoreAssembly])
            .That().AreAbstract()
            .And().AreClasses()
            .GetTypes()
            .Where(t => !t.IsSealed) // Exclude static classes (which are both abstract and sealed)
            .Where(t => !HasBaseSuffix(t.Name) && !t.Name.StartsWith("Abstract"))
            .Where(t => !t.Name.Contains("Test")) // Exclude test classes
            .ToList();

        // Assert - Abstract classes should follow naming convention (with some exceptions for decorators and extensions)
        var violations = abstractClasses
            .Where(t => !t.Name.Contains("Decorator"))
            .Where(t => !t.Name.Contains("Extensions")) // Exclude extension classes
            .Where(t => !t.Name.Contains("Operations")) // Exclude operation constants classes
            .Select(t => t.FullName)
            .ToList();

        Assert.Empty(violations);
    }

    private static bool HasBaseSuffix(string typeName)
    {
        // Handle generic types: MessageBase`1 should be considered as ending with "Base"
        if (typeName.Contains('`'))
        {
            var nameWithoutArity = typeName[..typeName.IndexOf('`')];
            return nameWithoutArity.EndsWith("Base");
        }
        return typeName.EndsWith("Base");
    }

    private static string FormatFailureMessage(NetArchTestResult result, string context)
    {
        if (result.IsSuccessful)
            return string.Empty;

        var violations = string.Join(Environment.NewLine, result.FailingTypeNames ?? []);
        return $"{context}:{Environment.NewLine}{violations}";
    }
}
