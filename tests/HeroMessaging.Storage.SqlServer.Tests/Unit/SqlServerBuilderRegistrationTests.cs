using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.ErrorHandling;
using HeroMessaging.Abstractions.Sagas;
using HeroMessaging.Abstractions.Storage;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeroMessaging.Storage.SqlServer.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class SqlServerBuilderRegistrationTests
{
    // No database is needed: registration, constructors, and disposal must remain lazy.
    private const string ConnectionString = "Server=127.0.0.1,1;Database=RegistrationOnly;Integrated Security=true;Connect Timeout=1";

    [Fact]
    public void UseSqlServer_WithConnectionString_RegistersDefaultsAndSingletonProviders()
    {
        var services = new ServiceCollection();
        var builder = services.AddHeroMessaging();

        Assert.Same(builder, builder.UseSqlServer(ConnectionString));

        using var provider = builder.Build().BuildServiceProvider();
        var options = provider.GetRequiredService<SqlServerStorageOptions>();
        Assert.Equal(ConnectionString, options.ConnectionString);
        Assert.Equal("dbo", options.Schema);
        Assert.Equal("Messages", options.MessagesTableName);
        Assert.Equal("OutboxMessages", options.OutboxTableName);
        Assert.Equal("DeadLetterQueue", options.DeadLetterTableName);
        Assert.True(options.AutoCreateTables);
        Assert.Equal(30, options.CommandTimeout);
        AssertAllStorageProviders(provider);
    }

    [Fact]
    public void UseSqlServer_WithOptions_PreservesOptionsAndSingletonProviders()
    {
        var builder = new ServiceCollection().AddHeroMessaging();
        var options = CreateOptions();

        Assert.Same(builder, builder.UseSqlServer(options));

        using var provider = builder.Build().BuildServiceProvider();
        Assert.Same(options, provider.GetRequiredService<SqlServerStorageOptions>());
        AssertAllStorageProviders(provider);
    }

    [Fact]
    public void UseSqlServer_WithConfigure_AppliesConfigurationOnceBeforeRegistration()
    {
        var builder = new ServiceCollection().AddHeroMessaging();
        var calls = 0;
        SqlServerStorageOptions? configuredOptions = null;

        var result = builder.UseSqlServer(ConnectionString, options =>
        {
            calls++;
            Assert.Equal(ConnectionString, options.ConnectionString);
            options.Schema = "messaging";
            options.MessagesTableName = "CustomMessages";
            options.AutoCreateTables = false;
            options.CommandTimeout = 17;
            configuredOptions = options;
        });

        Assert.Same(builder, result);
        Assert.Equal(1, calls);
        using var provider = builder.Build().BuildServiceProvider();
        var registeredOptions = provider.GetRequiredService<SqlServerStorageOptions>();
        Assert.Same(configuredOptions, registeredOptions);
        Assert.Equal("messaging", registeredOptions.Schema);
        Assert.Equal("CustomMessages", registeredOptions.MessagesTableName);
        Assert.False(registeredOptions.AutoCreateTables);
        Assert.Equal(17, registeredOptions.CommandTimeout);
        AssertAllStorageProviders(provider);
    }

    [Theory]
    [InlineData("message", typeof(IMessageStorage), typeof(SqlServerMessageStorage))]
    [InlineData("outbox", typeof(IOutboxStorage), typeof(SqlServerOutboxStorage))]
    [InlineData("deadLetter", typeof(IDeadLetterQueue), typeof(SqlServerDeadLetterQueue))]
    [InlineData("saga", typeof(ISagaRepository<RegistrationSaga>), typeof(SqlServerSagaRepository<RegistrationSaga>))]
    public void UseSqlServerSpecificStorage_WithOptions_RegistersOnlyRequestedSingleton(
        string registration, Type serviceType, Type implementationType)
    {
        var builder = new ServiceCollection().AddHeroMessaging();

        Assert.Same(builder, Register(registration, builder, CreateOptions()));

        var descriptor = Assert.Single(builder.Services);
        Assert.Equal(serviceType, descriptor.ServiceType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        using var provider = builder.Build().BuildServiceProvider();
        AssertSingletonProvider(provider, serviceType, implementationType);
    }

    [Theory]
    [InlineData("all")]
    [InlineData("message")]
    [InlineData("outbox")]
    [InlineData("deadLetter")]
    [InlineData("saga")]
    public void UseSqlServer_WithExplicitOptions_ProvidersConsumeSuppliedOptions(string registration)
    {
        var builder = new ServiceCollection().AddHeroMessaging();
        var options = CreateOptions();
        options.Schema = "invalid-schema";
        Register(registration, builder, options);

        using var provider = builder.Build().BuildServiceProvider();
        var serviceType = registration switch
        {
            "outbox" => typeof(IOutboxStorage),
            "deadLetter" => typeof(IDeadLetterQueue),
            "saga" => typeof(ISagaRepository<RegistrationSaga>),
            _ => typeof(IMessageStorage)
        };
        Assert.Throws<ArgumentException>(() => provider.GetRequiredService(serviceType));
    }

    [Fact]
    public void UseSqlServerSagaRepository_WithoutOptions_UsesOptionsRegisteredOnBuilder()
    {
        var builder = new ServiceCollection().AddHeroMessaging();
        var options = CreateOptions();

        Assert.Same(builder, builder.UseSqlServerSagaRepository<RegistrationSaga>());
        builder.UseSqlServer(options);

        using var provider = builder.Build().BuildServiceProvider();
        Assert.Same(options, provider.GetRequiredService<SqlServerStorageOptions>());
        AssertSingletonProvider(provider, typeof(ISagaRepository<RegistrationSaga>), typeof(SqlServerSagaRepository<RegistrationSaga>));
    }

    [Theory]
    [InlineData("connectionString")]
    [InlineData("configure")]
    [InlineData("all")]
    [InlineData("message")]
    [InlineData("outbox")]
    [InlineData("deadLetter")]
    [InlineData("saga")]
    [InlineData("defaultSaga")]
    public void UseSqlServer_WithNullBuilder_ThrowsArgumentNullException(string registration)
    {
        var exception = Assert.Throws<ArgumentNullException>(() => Register(registration, null!, CreateOptions()));

        Assert.Equal("builder", exception.ParamName);
    }

    [Theory]
    [InlineData("all")]
    [InlineData("message")]
    [InlineData("outbox")]
    [InlineData("deadLetter")]
    [InlineData("saga")]
    public void UseSqlServer_WithNullOptions_ThrowsBeforeChangingServices(string registration)
    {
        var builder = new ServiceCollection().AddHeroMessaging();

        var exception = Assert.Throws<ArgumentNullException>(() => Register(registration, builder, null!));

        Assert.Equal("options", exception.ParamName);
        Assert.Empty(builder.Services);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UseSqlServer_WithNullConnectionString_ThrowsBeforeConfigurationOrRegistration(bool withConfigure)
    {
        var builder = new ServiceCollection().AddHeroMessaging();
        var calls = 0;

        var exception = Assert.Throws<ArgumentNullException>(() => withConfigure
            ? builder.UseSqlServer(null!, _ => calls++)
            : builder.UseSqlServer((string)null!));

        Assert.Equal("connectionString", exception.ParamName);
        Assert.Equal(0, calls);
        Assert.Empty(builder.Services);
    }

    [Fact]
    public void UseSqlServer_WithNullConfigure_ThrowsBeforeChangingServices()
    {
        var builder = new ServiceCollection().AddHeroMessaging();

        var exception = Assert.Throws<ArgumentNullException>(() => builder.UseSqlServer(ConnectionString, null!));

        Assert.Equal("configure", exception.ParamName);
        Assert.Empty(builder.Services);
    }

    [Fact]
    public void UseSqlServer_WithNullBuilder_DoesNotInvokeConfigure()
    {
        var calls = 0;

        Assert.Throws<ArgumentNullException>(() => ((IHeroMessagingBuilder)null!).UseSqlServer(ConnectionString, _ => calls++));

        Assert.Equal(0, calls);
    }

    private static SqlServerStorageOptions CreateOptions() => new()
    {
        ConnectionString = ConnectionString,
        Schema = "messaging",
        MessagesTableName = "CustomMessages",
        OutboxTableName = "CustomOutbox",
        DeadLetterTableName = "CustomDeadLetters",
        SagasTableName = "CustomSagas",
        AutoCreateTables = false,
        CommandTimeout = 17
    };

    private static IHeroMessagingBuilder Register(string registration, IHeroMessagingBuilder builder, SqlServerStorageOptions options) => registration switch
    {
        "connectionString" => builder.UseSqlServer(ConnectionString),
        "configure" => builder.UseSqlServer(ConnectionString, _ => { }),
        "all" => builder.UseSqlServer(options),
        "message" => builder.UseSqlServerMessageStorage(options),
        "outbox" => builder.UseSqlServerOutbox(options),
        "deadLetter" => builder.UseSqlServerDeadLetterQueue(options),
        "saga" => builder.UseSqlServerSagaRepository<RegistrationSaga>(options),
        "defaultSaga" => builder.UseSqlServerSagaRepository<RegistrationSaga>(),
        _ => throw new ArgumentOutOfRangeException(nameof(registration))
    };

    private static void AssertAllStorageProviders(ServiceProvider provider)
    {
        AssertSingletonProvider(provider, typeof(IMessageStorage), typeof(SqlServerMessageStorage));
        AssertSingletonProvider(provider, typeof(IOutboxStorage), typeof(SqlServerOutboxStorage));
        AssertSingletonProvider(provider, typeof(IDeadLetterQueue), typeof(SqlServerDeadLetterQueue));
    }

    private static void AssertSingletonProvider(ServiceProvider provider, Type serviceType, Type implementationType)
    {
        var storage = provider.GetRequiredService(serviceType);
        Assert.IsType(implementationType, storage);
        Assert.Same(storage, provider.GetRequiredService(serviceType));
        using var scope = provider.CreateScope();
        Assert.Same(storage, scope.ServiceProvider.GetRequiredService(serviceType));
    }

    public sealed class RegistrationSaga : ISaga
    {
        public Guid CorrelationId { get; set; }
        public string CurrentState { get; set; } = "Started";
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public bool IsCompleted { get; set; }
        public int Version { get; set; }
    }
}
