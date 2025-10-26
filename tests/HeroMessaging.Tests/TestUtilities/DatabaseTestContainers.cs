using System;
using System.Data.Common;
using System.Threading.Tasks;
using Xunit;

namespace HeroMessaging.Tests.TestUtilities;

public abstract class DatabaseTestContainer : IAsyncDisposable
{
    protected string ConnectionString { get; set; }
    public bool IsRunning { get; protected set; }

    protected DatabaseTestContainer()
    {
        ConnectionString = string.Empty;
    }

    public abstract Task StartAsync();
    public abstract Task StopAsync();
    public abstract Task<bool> WaitForHealthCheckAsync(TimeSpan timeout);

    public virtual async ValueTask DisposeAsync()
    {
        if (IsRunning)
        {
            await StopAsync();
        }
        GC.SuppressFinalize(this);
    }

    protected abstract DbConnection CreateConnection();

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            var result = await command.ExecuteScalarAsync();
            return result != null;
        }
        catch
        {
            return false;
        }
    }
}

public class PostgreSqlTestContainer : DatabaseTestContainer
{
    private readonly string _containerName = $"postgres-test-{Guid.NewGuid():N}";
    private readonly int _port = Random.Shared.Next(5433, 5500);
    private readonly string _password = "Test123!";
    private readonly string _database = "herotest";
    private readonly string _username = "postgres";

    public override async Task StartAsync()
    {
        ConnectionString = $"Host=localhost;Port={_port};Database={_database};Username={_username};Password={_password}";

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "docker",
            Arguments = $"run -d --name {_containerName} -p {_port}:5432 " +
                       $"-e POSTGRES_PASSWORD={_password} -e POSTGRES_DB={_database} " +
                       "postgres:latest",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = System.Diagnostics.Process.Start(startInfo);
            await Task.Run(() => process?.WaitForExit());
            IsRunning = true;

            await WaitForHealthCheckAsync(TimeSpan.FromSeconds(30));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to start PostgreSQL container: {ex.Message}", ex);
        }
    }

    public override async Task StopAsync()
    {
        if (!IsRunning) return;

        try
        {
            var stopInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = $"stop {_containerName}",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var process = System.Diagnostics.Process.Start(stopInfo))
            {
                await Task.Run(() => process?.WaitForExit());
            }

            var removeInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = $"rm {_containerName}",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var process = System.Diagnostics.Process.Start(removeInfo))
            {
                await Task.Run(() => process?.WaitForExit());
            }

            IsRunning = false;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to stop PostgreSQL container: {ex.Message}", ex);
        }
    }

    public override async Task<bool> WaitForHealthCheckAsync(TimeSpan timeout)
    {
        var endTime = DateTime.UtcNow.Add(timeout);

        while (DateTime.UtcNow < endTime)
        {
            if (await TestConnectionAsync())
            {
                return true;
            }

            await Task.Delay(1000, TestContext.Current.CancellationToken);
        }

        return false;
    }

    protected override DbConnection CreateConnection()
    {
        throw new NotImplementedException("Add Npgsql package to use PostgreSQL connections");
    }

    public async Task ExecuteScriptAsync(string script)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = script;
        await command.ExecuteNonQueryAsync();
    }
}

public class SqlServerTestContainer : DatabaseTestContainer
{
    private readonly string _containerName = $"sqlserver-test-{Guid.NewGuid():N}";
    private readonly int _port = Random.Shared.Next(1434, 1500);
    private readonly string _password = "Test123!Strong";
    private readonly string _database = "herotest";

    public override async Task StartAsync()
    {
        ConnectionString = $"Server=localhost,{_port};Database={_database};User Id=sa;Password={_password};TrustServerCertificate=true";

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "docker",
            Arguments = $"run -d --name {_containerName} -p {_port}:1433 " +
                       $"-e ACCEPT_EULA=Y -e SA_PASSWORD={_password} " +
                       "mcr.microsoft.com/mssql/server:2022-latest",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = System.Diagnostics.Process.Start(startInfo);
            await Task.Run(() => process?.WaitForExit());
            IsRunning = true;

            if (await WaitForHealthCheckAsync(TimeSpan.FromSeconds(60)))
            {
                await CreateDatabaseAsync();
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to start SQL Server container: {ex.Message}", ex);
        }
    }

    public override async Task StopAsync()
    {
        if (!IsRunning) return;

        try
        {
            var stopInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = $"stop {_containerName}",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var process = System.Diagnostics.Process.Start(stopInfo))
            {
                await Task.Run(() => process?.WaitForExit());
            }

            var removeInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = $"rm {_containerName}",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var process = System.Diagnostics.Process.Start(removeInfo))
            {
                await Task.Run(() => process?.WaitForExit());
            }

            IsRunning = false;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to stop SQL Server container: {ex.Message}", ex);
        }
    }

    public override async Task<bool> WaitForHealthCheckAsync(TimeSpan timeout)
    {
        var endTime = DateTime.UtcNow.Add(timeout);
        var masterConnectionString = ConnectionString.Replace($"Database={_database}", "Database=master");

        while (DateTime.UtcNow < endTime)
        {
            try
            {
                using var connection = CreateConnectionWithString(masterConnectionString);
                await connection.OpenAsync();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1";
                var result = await command.ExecuteScalarAsync();
                if (result != null)
                {
                    return true;
                }
            }
            catch
            {
            }

            await Task.Delay(2000, TestContext.Current.CancellationToken);
        }

        return false;
    }

    protected override DbConnection CreateConnection()
    {
        return CreateConnectionWithString(ConnectionString);
    }

    private DbConnection CreateConnectionWithString(string connectionString)
    {
        throw new NotImplementedException("Add Microsoft.Data.SqlClient package to use SQL Server connections");
    }

    private async Task CreateDatabaseAsync()
    {
        var masterConnectionString = ConnectionString.Replace($"Database={_database}", "Database=master");
        using var connection = CreateConnectionWithString(masterConnectionString);
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = $"IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = '{_database}') CREATE DATABASE [{_database}]";
        await command.ExecuteNonQueryAsync();
    }
}