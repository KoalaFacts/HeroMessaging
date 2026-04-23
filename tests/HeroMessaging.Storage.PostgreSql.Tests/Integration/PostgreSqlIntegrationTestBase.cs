using Testcontainers.PostgreSql;
using Xunit;

namespace HeroMessaging.Storage.PostgreSql.Tests.Integration;

/// <summary>
/// Base class for PostgreSQL integration tests using Testcontainers
/// </summary>
public abstract class PostgreSqlIntegrationTestBase : IAsyncLifetime
{
    protected PostgreSqlContainer? Container;
    protected PostgreSqlStorageOptions? Options;
    private string? _connectionString;
    protected string ConnectionString => _connectionString ?? throw new InvalidOperationException("Test not initialized");

    public async ValueTask InitializeAsync()
    {
        // Use environment variable connection string if available (CI environment)
        var envConnectionString = Environment.GetEnvironmentVariable("PostgreSql__ConnectionString");
        if (!string.IsNullOrEmpty(envConnectionString))
        {
            _connectionString = envConnectionString;
        }
        else
        {
            // Fall back to Testcontainers for local development
            Container = new PostgreSqlBuilder("postgres:17-alpine")
                .WithPassword("postgres")
                .Build();

            await Container.StartAsync(TestContext.Current.CancellationToken);

            _connectionString = Container.GetConnectionString();
        }

        Options = new PostgreSqlStorageOptions
        {
            ConnectionString = _connectionString,
            Schema = "test",
            AutoCreateTables = true
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (Container != null)
        {
            await Container.StopAsync(TestContext.Current.CancellationToken);
            await Container.DisposeAsync();
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
