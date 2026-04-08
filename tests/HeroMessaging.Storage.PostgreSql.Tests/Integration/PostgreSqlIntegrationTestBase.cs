using HeroMessaging.Tests.Shared.Infrastructure;
using Testcontainers.PostgreSql;
using Xunit;

namespace HeroMessaging.Storage.PostgreSql.Tests.Integration;

/// <summary>
/// Base class for PostgreSQL integration tests using Testcontainers
/// </summary>
public abstract class PostgreSqlIntegrationTestBase : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    protected PostgreSqlStorageOptions? Options;
    private string? _connectionString;
    protected string ConnectionString => _connectionString ?? throw new InvalidOperationException("Test not initialized");

    public async ValueTask InitializeAsync()
    {
        // Use environment variable connection string if available (CI environment)
        var envConnectionString = TestDatabaseEnvironment.GetConnectionStringFromEnvironment(
            TestDatabaseEnvironment.PostgreSqlConnectionStringEnvVar);

        if (envConnectionString is not null)
        {
            _connectionString = envConnectionString;
        }
        else
        {
            // Fall back to Testcontainers for local development
            _container = new PostgreSqlBuilder()
                .WithImage(TestDatabaseEnvironment.PostgreSqlImage)
                .WithPassword(TestDatabaseEnvironment.PostgreSqlPassword)
                .Build();

            await _container.StartAsync(TestContext.Current.CancellationToken);

            _connectionString = _container.GetConnectionString();
        }

        Options = new PostgreSqlStorageOptions
        {
            ConnectionString = _connectionString,
            Schema = TestDatabaseEnvironment.DefaultTestSchema,
            AutoCreateTables = true
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (_container != null)
        {
            await _container.StopAsync(TestContext.Current.CancellationToken);
            await _container.DisposeAsync();
        }
    }

    protected PostgreSqlMessageStorage CreateMessageStorage(TimeProvider? timeProvider = null)
    {
        if (Options == null)
        {
            throw new InvalidOperationException("Test not initialized");
        }
        var jsonSerializer = new Utilities.DefaultJsonSerializer(new Utilities.DefaultBufferPoolManager());
        return new PostgreSqlMessageStorage(Options, timeProvider ?? TimeProvider.System, jsonSerializer);
    }
}
