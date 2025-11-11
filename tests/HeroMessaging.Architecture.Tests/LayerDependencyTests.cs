namespace HeroMessaging.Architecture.Tests;

/// <summary>
/// Tests to ensure proper layering and dependency direction.
/// Abstractions should never depend on implementation details.
/// </summary>
public class LayerDependencyTests
{
    private static readonly Assembly AbstractionsAssembly = typeof(Abstractions.Messages.IMessage).Assembly;

    [Fact]
    [Trait("Category", "Architecture")]
    public void Abstractions_ShouldNotDependOnImplementation()
    {
        // Arrange & Act
        var result = Types.InAssembly(AbstractionsAssembly)
            .Should()
            .NotHaveDependencyOn("HeroMessaging.Storage.SqlServer")
            .And().NotHaveDependencyOn("HeroMessaging.Storage.PostgreSql")
            .And().NotHaveDependencyOn("HeroMessaging.Serialization.Json")
            .And().NotHaveDependencyOn("HeroMessaging.Serialization.MessagePack")
            .And().NotHaveDependencyOn("HeroMessaging.Serialization.Protobuf")
            .And().NotHaveDependencyOn("HeroMessaging.Transport.RabbitMQ")
            .And().NotHaveDependencyOn("HeroMessaging.Observability.OpenTelemetry")
            .And().NotHaveDependencyOn("HeroMessaging.Observability.HealthChecks")
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
            .Should()
            .NotHaveDependencyOn("HeroMessaging.Processing")
            .And().NotHaveDependencyOn("HeroMessaging.Sagas")
            .And().NotHaveDependencyOn("HeroMessaging.Scheduling")
            .GetResult();

        // Assert
        Assert.True(result.IsSuccessful, FormatFailureMessage(result));
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void StoragePlugins_ShouldOnlyDependOnAbstractions()
    {
        // Arrange
        var sqlServerAssembly = typeof(Storage.SqlServer.SqlServerConnectionProvider).Assembly;
        var postgresAssembly = typeof(Storage.PostgreSql.PostgreSqlConnectionProvider).Assembly;

        // Act
        var sqlServerResult = Types.InAssembly(sqlServerAssembly)
            .Should().NotHaveDependencyOn("HeroMessaging.Storage.PostgreSql")
            .And().NotHaveDependencyOn("HeroMessaging.Transport.RabbitMQ")
            .GetResult();

        var postgresResult = Types.InAssembly(postgresAssembly)
            .Should().NotHaveDependencyOn("HeroMessaging.Storage.SqlServer")
            .And().NotHaveDependencyOn("HeroMessaging.Transport.RabbitMQ")
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
            .Should().NotHaveDependencyOn("HeroMessaging.Serialization.MessagePack")
            .And().NotHaveDependencyOn("HeroMessaging.Serialization.Protobuf")
            .GetResult();

        var messagePackResult = Types.InAssembly(messagePack)
            .Should().NotHaveDependencyOn("HeroMessaging.Serialization.Json")
            .And().NotHaveDependencyOn("HeroMessaging.Serialization.Protobuf")
            .GetResult();

        var protobufResult = Types.InAssembly(protobuf)
            .Should().NotHaveDependencyOn("HeroMessaging.Serialization.Json")
            .And().NotHaveDependencyOn("HeroMessaging.Serialization.MessagePack")
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
            ("SqlServer", typeof(Storage.SqlServer.SqlServerConnectionProvider).Assembly),
            ("PostgreSql", typeof(Storage.PostgreSql.PostgreSqlConnectionProvider).Assembly),
            ("Json", typeof(Serialization.Json.JsonMessageSerializer).Assembly),
            ("MessagePack", typeof(Serialization.MessagePack.MessagePackMessageSerializer).Assembly),
            ("Protobuf", typeof(Serialization.Protobuf.ProtobufMessageSerializer).Assembly),
            ("RabbitMQ", typeof(Transport.RabbitMQ.RabbitMqTransport).Assembly),
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

    private static string FormatFailureMessage(NetArchTestResult result)
    {
        if (result.IsSuccessful)
            return string.Empty;

        var violations = string.Join(Environment.NewLine, result.FailingTypeNames ?? Array.Empty<string>());
        return $"Architecture violation detected:{Environment.NewLine}{violations}";
    }
}
