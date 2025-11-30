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
    protected string ConnectionString => Container?.GetConnectionString() ?? throw new InvalidOperationException("Container not initialized");

    public async ValueTask InitializeAsync()
    {
        Container = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithPassword("postgres")
            .Build();

        await Container.StartAsync();

        Options = new PostgreSqlStorageOptions
        {
            ConnectionString = Container.GetConnectionString(),
            Schema = "test",
            AutoCreateTables = true
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (Container != null)
        {
            await Container.StopAsync();
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
