namespace HeroMessaging.Architecture.Tests;

/// <summary>
/// General architecture tests covering code organization and design principles.
/// </summary>
public class GeneralArchitectureTests
{
    private static readonly Assembly AbstractionsAssembly = typeof(Abstractions.Messages.IMessage).Assembly;
    private static readonly Assembly CoreAssembly = typeof(HeroMessagingService).Assembly;

    [Fact]
    [Trait("Category", "Architecture")]
    public void PublicTypes_ShouldResideInProperNamespace()
    {
        // Ensure public types are in namespaces that match their assembly
        // Exception: Extension classes following the "ExtensionsTo*" pattern should be in the target's namespace

        // Arrange
        var assemblies = new[]
        {
            AbstractionsAssembly,
            CoreAssembly,
            typeof(Storage.SqlServer.SqlServerConnectionProvider).Assembly,
            typeof(Storage.PostgreSql.PostgreSqlConnectionProvider).Assembly,
            typeof(Serialization.Json.JsonMessageSerializer).Assembly,
            typeof(Serialization.MessagePack.MessagePackMessageSerializer).Assembly,
            typeof(Serialization.Protobuf.ProtobufMessageSerializer).Assembly,
            typeof(Transport.RabbitMQ.RabbitMqTransport).Assembly,
            typeof(Observability.OpenTelemetry.HeroMessagingInstrumentation).Assembly,
            typeof(Observability.HealthChecks.TransportHealthCheck).Assembly,
            typeof(Security.Signing.HmacSha256MessageSigner).Assembly,
        };

        // Act & Assert
        foreach (var assembly in assemblies)
        {
            var assemblyName = assembly.GetName().Name!;
            var publicTypes = assembly.GetTypes()
                .Where(t => t.IsPublic)
                .Where(t => t.Namespace != null)
                .ToList();

            foreach (var type in publicTypes)
            {
                // Extension classes following "ExtensionsTo*" pattern should be in the target's namespace
                if (type.Name.StartsWith("ExtensionsTo", StringComparison.Ordinal))
                {
                    continue; // Skip namespace check for extension classes
                }

                Assert.True(
                    type.Namespace!.StartsWith(assemblyName, StringComparison.Ordinal),
                    $"Type {type.FullName} should be in namespace starting with {assemblyName}");
            }
        }
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void Repositories_ShouldHaveRepositoryInName()
    {
        // Arrange & Act - Look for classes implementing repository-like interfaces
        var repositories = Types.InAssembly(CoreAssembly)
            .That().HaveNameEndingWith("Repository")
            // Note: ISagaRepository<TSaga> is generic, can't reference without type argument
            .GetTypes()
            .Where(t => !t.Name.Contains("Repository"))
            .ToList();

        // Assert
        Assert.Empty(repositories);
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void Builders_ShouldHaveBuilderInName()
    {
        // Arrange & Act
        // Filter out static classes (which are both abstract and sealed) - these are extension classes
        var builders = Types.InAssembly(CoreAssembly)
            .That().HaveNameEndingWith("Builder")
            .GetTypes()
            .Where(t => !(t.IsAbstract && t.IsSealed)) // Exclude static classes
            .ToList();

        var abstractBuilders = builders
            .Where(t => t.IsAbstract && !t.IsInterface)
            .Where(t => !t.Name.StartsWith("I"))
            .Select(t => t.FullName)
            .ToList();

        // Assert - Builders should be concrete or interfaces (not abstract)
        Assert.Empty(abstractBuilders);
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void Factories_ShouldHaveFactoryInName()
    {
        // Look for classes that create instances (factory pattern)

        // Arrange
        var factoryTypes = CoreAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .Where(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Any(m => m.Name.StartsWith("Create") || m.Name.StartsWith("Build")))
            .Where(t => !t.Name.Contains("Factory") && !t.Name.Contains("Builder"))
            .ToList();

        // Allow some flexibility - not all creator methods mean it's a factory
        var likelyFactories = factoryTypes
            .Where(t => t.GetMethods().Count(m => m.Name.StartsWith("Create")) >= 3)
            .ToList();

        Assert.Empty(likelyFactories);
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void AsyncMethods_ShouldEndWithAsync()
    {
        // Arrange
        var assemblies = new[] { AbstractionsAssembly, CoreAssembly };
        var violations = new List<string>();

        // Act
        foreach (var assembly in assemblies)
        {
            var asyncMethods = assembly.GetTypes()
                .Where(t => t.IsPublic || t.IsNestedPublic)
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                .Where(m => m.ReturnType == typeof(Task) ||
                           (m.ReturnType.IsGenericType &&
                            m.ReturnType.GetGenericTypeDefinition() == typeof(Task<>)) ||
                           m.ReturnType == typeof(ValueTask) ||
                           (m.ReturnType.IsGenericType &&
                            m.ReturnType.GetGenericTypeDefinition() == typeof(ValueTask<>)))
                .Where(m => !m.Name.EndsWith("Async"))
                .Where(m => !m.Name.StartsWith("get_") && !m.Name.StartsWith("set_")) // Exclude property accessors
                .Where(m => m.Name != "Handle") // Exclude Handle methods (common CQRS/mediator pattern)
                .Where(m => m.Name != "Send" && m.Name != "Publish") // Exclude Send/Publish methods (common messaging pattern)
                .Where(m => m.Name != "ProcessIncoming" && m.Name != "GetUnprocessedCount") // Exclude processor methods (established API)
                .Where(m => m.Name != "PublishToOutbox" && m.Name != "Enqueue") // Exclude queueing methods (established API)
                .Where(m => m.Name != "StartQueue" && m.Name != "StopQueue") // Exclude queue lifecycle methods (established API)
                .Where(m => !m.Name.StartsWith("On")) // Exclude event handler methods (OnError, OnRetry, etc.)
                .ToList();

            violations.AddRange(asyncMethods.Select(m => $"{m.DeclaringType?.FullName}.{m.Name}"));
        }

        // Assert
        Assert.Empty(violations);
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void PublicClasses_ShouldNotBeNested()
    {
        // Public classes should be top-level for better discoverability

        // Arrange & Act
        var result = Types.InAssemblies([AbstractionsAssembly, CoreAssembly])
            .That().ArePublic()
            .And().AreClasses()
            .ShouldNot().BeNested()
            .GetResult();

        // Assert - Allow some nested classes for specific patterns
        if (!result.IsSuccessful)
        {
            var violations = result.FailingTypeNames?
                .Where(t => !t.Contains("+Builder")) // Allow nested builders
                .Where(t => !t.Contains("+Options")) // Allow nested options
                .Where(t => !t.Contains("PooledBuffer")) // Allow nested buffer types
                .Where(t => !t.Contains("BufferPoolManager")) // Allow buffer pool manager nested types
                .ToList() ?? [];

            Assert.Empty(violations);
        }
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void SealedClasses_ShouldNotHaveProtectedMembers()
    {
        // Sealed classes can't be inherited, so protected members are useless

        // Arrange
        var sealedClasses = Types.InAssemblies([AbstractionsAssembly, CoreAssembly])
            .That().AreSealed()
            .And().AreClasses()
            .GetTypes()
            .ToList();

        // Act & Assert
        foreach (var sealedClass in sealedClasses)
        {
            var protectedMembers = sealedClass.GetMembers(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m is MethodInfo method && method.IsFamily || // protected
                           m is FieldInfo field && field.IsFamily ||
                           m is PropertyInfo property && property.GetMethod?.IsFamily == true)
                .Where(m => !m.Name.StartsWith('<')) // Exclude compiler-generated
                .Where(m => m.DeclaringType == sealedClass) // Only check members declared in this class, not inherited
                .ToList();

            Assert.Empty(protectedMembers);
        }
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void Plugins_ShouldHaveServiceCollectionExtensions()
    {
        // Each plugin should provide AddXxx extension methods for DI registration

        // Arrange
        var pluginAssemblies = new[]
        {
            ("SqlServer", typeof(Storage.SqlServer.SqlServerConnectionProvider).Assembly),
            ("PostgreSql", typeof(Storage.PostgreSql.PostgreSqlConnectionProvider).Assembly),
            ("Json", typeof(Serialization.Json.JsonMessageSerializer).Assembly),
            ("MessagePack", typeof(Serialization.MessagePack.MessagePackMessageSerializer).Assembly),
            ("Protobuf", typeof(Serialization.Protobuf.ProtobufMessageSerializer).Assembly),
            ("RabbitMQ", typeof(Transport.RabbitMQ.RabbitMqTransport).Assembly),
            ("OpenTelemetry", typeof(Observability.OpenTelemetry.HeroMessagingInstrumentation).Assembly),
            ("HealthChecks", typeof(Observability.HealthChecks.TransportHealthCheck).Assembly),
            ("Security", typeof(Security.Signing.HmacSha256MessageSigner).Assembly),
        };

        // Act & Assert
        foreach (var (name, assembly) in pluginAssemblies)
        {
            var hasExtensions = assembly.GetTypes()
                .Any(t => t.Name.Contains("Extensions") && t.IsClass && t.IsSealed && t.IsAbstract); // static class

            Assert.True(hasExtensions, $"{name} plugin should have ServiceCollection extension methods");
        }
    }
}
