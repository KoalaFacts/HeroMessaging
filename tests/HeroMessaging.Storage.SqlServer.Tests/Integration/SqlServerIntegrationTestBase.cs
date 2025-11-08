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
    protected string ConnectionString => Container?.GetConnectionString() ?? throw new InvalidOperationException("Container not initialized");

    public async ValueTask InitializeAsync()
    {
        Container = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .WithPassword("YourStrong@Passw0rd")
            .Build();

        await Container.StartAsync();

        Options = new SqlServerStorageOptions
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

    protected SqlServerMessageStorage CreateMessageStorage(TimeProvider? timeProvider = null)
    {
        if (Options == null)
        {
            throw new InvalidOperationException("Test not initialized");
        }
        return new SqlServerMessageStorage(Options, timeProvider ?? TimeProvider.System);
    }
}
