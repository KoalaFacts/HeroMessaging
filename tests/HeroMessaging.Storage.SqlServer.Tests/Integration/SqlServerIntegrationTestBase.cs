using HeroMessaging.Tests.Shared.Infrastructure;
using Testcontainers.MsSql;
using Xunit;

namespace HeroMessaging.Storage.SqlServer.Tests.Integration;

/// <summary>
/// Base class for SQL Server integration tests using Testcontainers
/// </summary>
public abstract class SqlServerIntegrationTestBase : IAsyncLifetime
{
    private MsSqlContainer? _container;
    protected SqlServerStorageOptions? Options;
    private string? _connectionString;
    protected string ConnectionString => _connectionString ?? throw new InvalidOperationException("Test not initialized");

    public async ValueTask InitializeAsync()
    {
        // Use environment variable connection string if available (CI environment)
        var envConnectionString = TestDatabaseEnvironment.GetConnectionStringFromEnvironment(
            TestDatabaseEnvironment.SqlServerConnectionStringEnvVar);

        if (envConnectionString is not null)
        {
            _connectionString = envConnectionString;
        }
        else
        {
            // Fall back to Testcontainers for local development
            _container = new MsSqlBuilder()
                .WithImage(TestDatabaseEnvironment.SqlServerImage)
                .WithPassword(TestDatabaseEnvironment.SqlServerPassword)
                .Build();

            await _container.StartAsync(TestContext.Current.CancellationToken);

            _connectionString = _container.GetConnectionString();
        }

        Options = new SqlServerStorageOptions
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

    protected SqlServerMessageStorage CreateMessageStorage(TimeProvider? timeProvider = null)
    {
        if (Options == null)
        {
            throw new InvalidOperationException("Test not initialized");
        }
        var jsonSerializer = new Utilities.DefaultJsonSerializer(new Utilities.DefaultBufferPoolManager());
        return new SqlServerMessageStorage(Options, timeProvider ?? TimeProvider.System, jsonSerializer);
    }
}
