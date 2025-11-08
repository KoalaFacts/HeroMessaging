namespace HeroMessaging.ArchitectureTests;

/// <summary>
/// General architecture tests covering code organization and design principles.
/// </summary>
public class GeneralArchitectureTests
{
    private static readonly Assembly AbstractionsAssembly = typeof(Abstractions.IMessage).Assembly;
    private static readonly Assembly CoreAssembly = typeof(HeroMessagingService).Assembly;

    [Fact]
    [Trait("Category", "Architecture")]
    public void PublicTypes_ShouldResideInProperNamespace()
    {
        // Ensure public types are in namespaces that match their assembly

        // Arrange
        var assemblies = new[]
        {
            AbstractionsAssembly,
            CoreAssembly,
            typeof(Storage.SqlServer.SqlServerDbConnectionProvider).Assembly,
            typeof(Storage.PostgreSql.PostgreSqlDbConnectionProvider).Assembly,
            typeof(Serialization.Json.JsonMessageSerializer).Assembly,
            typeof(Serialization.MessagePack.MessagePackMessageSerializer).Assembly,
            typeof(Serialization.Protobuf.ProtobufMessageSerializer).Assembly,
            typeof(Transport.RabbitMQ.RabbitMQTransport).Assembly,
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
            .Or().ImplementInterface(typeof(Abstractions.Sagas.ISagaRepository))
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
        var result = Types.InAssembly(CoreAssembly)
            .That().HaveNameEndingWith("Builder")
            .Should().NotBeAbstract()
            .Or().HaveNameStartingWith("I") // Interface builders are OK
            .GetResult();

        // Assert - Builders should be concrete or interfaces
        Assert.True(result.IsSuccessful, FormatFailureMessage(result));
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
        var result = Types.InAssemblies(new[] { AbstractionsAssembly, CoreAssembly })
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
                .ToList() ?? new List<string>();

            Assert.Empty(violations);
        }
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void SealedClasses_ShouldNotHaveProtectedMembers()
    {
        // Sealed classes can't be inherited, so protected members are useless

        // Arrange
        var sealedClasses = Types.InAssemblies(new[] { AbstractionsAssembly, CoreAssembly })
            .That().AreSealed()
            .And().AreClasses()
            .GetTypes()
            .ToList();

        // Act & Assert
        foreach (var sealedClass in sealedClasses)
        {
            var protectedMembers = sealedClass.GetMembers(BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(m => m is MethodInfo method && method.IsFamily || // protected
                           m is FieldInfo field && field.IsFamily ||
                           m is PropertyInfo property && property.GetMethod?.IsFamily == true)
                .Where(m => !m.Name.StartsWith("<")) // Exclude compiler-generated
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
            ("SqlServer", typeof(Storage.SqlServer.SqlServerDbConnectionProvider).Assembly),
            ("PostgreSql", typeof(Storage.PostgreSql.PostgreSqlDbConnectionProvider).Assembly),
            ("Json", typeof(Serialization.Json.JsonMessageSerializer).Assembly),
            ("MessagePack", typeof(Serialization.MessagePack.MessagePackMessageSerializer).Assembly),
            ("Protobuf", typeof(Serialization.Protobuf.ProtobufMessageSerializer).Assembly),
            ("RabbitMQ", typeof(Transport.RabbitMQ.RabbitMQTransport).Assembly),
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

    private static string FormatFailureMessage(TestResult result)
    {
        if (result.IsSuccessful)
            return string.Empty;

        var violations = string.Join(Environment.NewLine, result.FailingTypeNames ?? Array.Empty<string>());
        return $"Architecture violation:{Environment.NewLine}{violations}";
    }
}
