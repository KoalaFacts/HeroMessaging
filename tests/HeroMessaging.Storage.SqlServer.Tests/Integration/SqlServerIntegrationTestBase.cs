using Testcontainers.MsSql;
using Xunit;

namespace HeroMessaging.Storage.SqlServer.Tests.Integration;

/// <summary>
/// Base class for SQL Server integration tests using Testcontainers
/// </summary>
public abstract class SqlServerIntegrationTestBase : IAsyncLifetime
{
    protected MsSqlContainer? Container;
    protected SqlServerStorageOptions? Options;
    private string? _connectionString;
    protected string ConnectionString => _connectionString ?? throw new InvalidOperationException("Test not initialized");

    public async ValueTask InitializeAsync()
    {
        // Use environment variable connection string if available (CI environment)
        var envConnectionString = Environment.GetEnvironmentVariable("SqlServer__ConnectionString");
        if (!string.IsNullOrEmpty(envConnectionString))
        {
            _connectionString = envConnectionString;
        }
        else
        {
            // Fall back to Testcontainers for local development
            Container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
                .WithPassword("YourStrong@Passw0rd")
                .Build();

            await Container.StartAsync(TestContext.Current.CancellationToken);

            _connectionString = Container.GetConnectionString();
        }

        Options = new SqlServerStorageOptions
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
