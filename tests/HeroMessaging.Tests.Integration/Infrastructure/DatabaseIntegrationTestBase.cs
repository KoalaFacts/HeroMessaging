using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Xunit;

namespace HeroMessaging.Tests.Integration.Infrastructure;

public abstract class DatabaseIntegrationTestBase : IntegrationTestBase
{
    private MsSqlContainer? _msSqlContainer;
    private PostgreSqlContainer? _postgreSqlContainer;
    protected DatabaseProvider DatabaseProvider { get; set; }

    protected string ConnectionString { get; private set; } = string.Empty;

    protected DatabaseIntegrationTestBase(DatabaseProvider databaseProvider)
    {
        DatabaseProvider = databaseProvider;
    }

    public override async ValueTask InitializeAsync()
    {
        await StartDatabaseContainerAsync();
        await base.InitializeAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await StopDatabaseContainerAsync();
    }

    private async Task StartDatabaseContainerAsync()
    {
        switch (DatabaseProvider)
        {
            case DatabaseProvider.SqlServer:
                _msSqlContainer = new MsSqlBuilder()
                    .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
                    .Build();
                await _msSqlContainer.StartAsync();
                ConnectionString = _msSqlContainer.GetConnectionString();
                break;

            case DatabaseProvider.PostgreSql:
                _postgreSqlContainer = new PostgreSqlBuilder()
                    .WithImage("postgres:16-alpine")
                    .Build();
                await _postgreSqlContainer.StartAsync();
                ConnectionString = _postgreSqlContainer.GetConnectionString();
                break;

            default:
                throw new NotSupportedException($"Database provider {DatabaseProvider} is not supported.");
        }
    }

    private async Task StopDatabaseContainerAsync()
    {
        if (_msSqlContainer != null)
        {
            await _msSqlContainer.DisposeAsync();
        }

        if (_postgreSqlContainer != null)
        {
            await _postgreSqlContainer.DisposeAsync();
        }
    }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        
        // Initialize database schema
        await InitializeDatabaseSchemaAsync();
    }

    protected virtual Task InitializeDatabaseSchemaAsync()
    {
        // Override in derived classes to set up database schema
        return Task.CompletedTask;
    }
}

public enum DatabaseProvider
{
    SqlServer,
    PostgreSql
}