namespace HeroMessaging.ArchitectureTests;

/// <summary>
/// Tests to ensure proper layering and dependency direction.
/// Abstractions should never depend on implementation details.
/// </summary>
public class LayerDependencyTests
{
    private static readonly Assembly AbstractionsAssembly = typeof(Abstractions.IMessage).Assembly;

    [Fact]
    [Trait("Category", "Architecture")]
    public void Abstractions_ShouldNotDependOnImplementation()
    {
        // Arrange & Act
        var result = Types.InAssembly(AbstractionsAssembly)
            .ShouldNot()
            .HaveDependencyOn("HeroMessaging.Storage.SqlServer")
            .And().ShouldNot().HaveDependencyOn("HeroMessaging.Storage.PostgreSql")
            .And().ShouldNot().HaveDependencyOn("HeroMessaging.Serialization.Json")
            .And().ShouldNot().HaveDependencyOn("HeroMessaging.Serialization.MessagePack")
            .And().ShouldNot().HaveDependencyOn("HeroMessaging.Serialization.Protobuf")
            .And().ShouldNot().HaveDependencyOn("HeroMessaging.Transport.RabbitMQ")
            .And().ShouldNot().HaveDependencyOn("HeroMessaging.Observability.OpenTelemetry")
            .And().ShouldNot().HaveDependencyOn("HeroMessaging.Observability.HealthChecks")
            .GetResult();

        // Assert
        Assert.True(result.IsSuccessful, FormatFailureMessage(result));
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void Abstractions_ShouldNotDependOnCoreImplementation()
    {
        // Arrange & Act
        var result = Types.InAssembly(AbstractionsAssembly)
            .That()
            .ResideInNamespace("HeroMessaging.Abstractions")
            .ShouldNot()
            .HaveDependencyOn("HeroMessaging.Processing")
            .And().ShouldNot().HaveDependencyOn("HeroMessaging.Sagas")
            .And().ShouldNot().HaveDependencyOn("HeroMessaging.Scheduling")
            .GetResult();

        // Assert
        Assert.True(result.IsSuccessful, FormatFailureMessage(result));
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void StoragePlugins_ShouldOnlyDependOnAbstractions()
    {
        // Arrange
        var sqlServerAssembly = typeof(Storage.SqlServer.SqlServerDbConnectionProvider).Assembly;
        var postgresAssembly = typeof(Storage.PostgreSql.PostgreSqlDbConnectionProvider).Assembly;

        // Act
        var sqlServerResult = Types.InAssembly(sqlServerAssembly)
            .ShouldNot().HaveDependencyOn("HeroMessaging.Storage.PostgreSql")
            .And().ShouldNot().HaveDependencyOn("HeroMessaging.Transport.RabbitMQ")
            .GetResult();

        var postgresResult = Types.InAssembly(postgresAssembly)
            .ShouldNot().HaveDependencyOn("HeroMessaging.Storage.SqlServer")
            .And().ShouldNot().HaveDependencyOn("HeroMessaging.Transport.RabbitMQ")
            .GetResult();

        // Assert
        Assert.True(sqlServerResult.IsSuccessful, FormatFailureMessage(sqlServerResult));
        Assert.True(postgresResult.IsSuccessful, FormatFailureMessage(postgresResult));
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void SerializationPlugins_ShouldNotDependOnEachOther()
    {
        // Arrange
        var jsonAssembly = typeof(Serialization.Json.JsonMessageSerializer).Assembly;
        var messagePack = typeof(Serialization.MessagePack.MessagePackMessageSerializer).Assembly;
        var protobuf = typeof(Serialization.Protobuf.ProtobufMessageSerializer).Assembly;

        // Act
        var jsonResult = Types.InAssembly(jsonAssembly)
            .ShouldNot().HaveDependencyOn("HeroMessaging.Serialization.MessagePack")
            .And().ShouldNot().HaveDependencyOn("HeroMessaging.Serialization.Protobuf")
            .GetResult();

        var messagePackResult = Types.InAssembly(messagePack)
            .ShouldNot().HaveDependencyOn("HeroMessaging.Serialization.Json")
            .And().ShouldNot().HaveDependencyOn("HeroMessaging.Serialization.Protobuf")
            .GetResult();

        var protobufResult = Types.InAssembly(protobuf)
            .ShouldNot().HaveDependencyOn("HeroMessaging.Serialization.Json")
            .And().ShouldNot().HaveDependencyOn("HeroMessaging.Serialization.MessagePack")
            .GetResult();

        // Assert
        Assert.True(jsonResult.IsSuccessful, FormatFailureMessage(jsonResult));
        Assert.True(messagePackResult.IsSuccessful, FormatFailureMessage(messagePackResult));
        Assert.True(protobufResult.IsSuccessful, FormatFailureMessage(protobufResult));
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void Plugins_ShouldNotReferenceOtherPlugins()
    {
        // This ensures plugin isolation - each plugin is independently loadable

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
        };

        foreach (var (name, assembly) in pluginAssemblies)
        {
            var dependencies = assembly.GetReferencedAssemblies()
                .Where(a => a.Name?.StartsWith("HeroMessaging.") == true)
                .Where(a => a.Name != "HeroMessaging.Abstractions" && a.Name != "HeroMessaging")
                .ToList();

            Assert.Empty(dependencies);
        }
    }

    private static string FormatFailureMessage(TestResult result)
    {
        if (result.IsSuccessful)
            return string.Empty;

        var violations = string.Join(Environment.NewLine, result.FailingTypeNames ?? Array.Empty<string>());
        return $"Architecture violation detected:{Environment.NewLine}{violations}";
    }
}
