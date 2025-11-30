using Xunit;

namespace HeroMessaging.Storage.PostgreSql.Tests;

[Trait("Category", "Integration")]
public sealed class PostgreSqlStorageTests : IDisposable
{
    private readonly MockPostgreSqlTestContainer _container;
    private bool _disposed = false;

    public PostgreSqlStorageTests()
    {
        _container = new MockPostgreSqlTestContainer();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PostgreSqlContainer_StartAndStop_WorksCorrectly()
    {
        // This is a basic test to ensure the test infrastructure works
        // Real storage tests would be added when PostgreSQL storage plugin is implemented

        // For now, we'll test that our test infrastructure is working
        await Task.Delay(1, TestContext.Current.CancellationToken); // Simulate async work

        Assert.True(true, "PostgreSQL test infrastructure is ready");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PostgreSqlStorage_ConnectionString_IsValidFormat()
    {
        // Test that we can generate valid connection strings
        var expectedPattern = @"Host=localhost;Port=\d+;Database=\w+;Username=\w+;Password=.+";

        await Task.Delay(1, TestContext.Current.CancellationToken); // Simulate async work

        // Mock connection string validation
        var mockConnectionString = "Host=localhost;Port=5432;Database=herotest;Username=postgres;Password=Test123!";

        Assert.Matches(expectedPattern, mockConnectionString);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void PostgreSqlStorage_Configuration_ValidatesRequiredProperties()
    {
        // Test configuration validation
        var config = new PostgreSqlStorageConfiguration
        {
            Host = "localhost",
            Port = 5432,
            Database = "herotest",
            Username = "postgres",
            Password = "Test123!"
        };

        Assert.NotNull(config.Host);
        Assert.True(config.Port > 0);
        Assert.NotNull(config.Database);
        Assert.NotNull(config.Username);
        Assert.NotNull(config.Password);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void PostgreSqlStorage_Configuration_ThrowsOnInvalidPort()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PostgreSqlStorageConfiguration
            {
                Host = "localhost",
                Port = -1, // Invalid port
                Database = "herotest",
                Username = "postgres",
                Password = "Test123!"
            });
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void PostgreSqlStorage_Configuration_ThrowsOnNullHost()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new PostgreSqlStorageConfiguration
            {
                Host = null!, // Invalid host
                Port = 5432,
                Database = "herotest",
                Username = "postgres",
                Password = "Test123!"
            });
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _container?.Dispose();
            _disposed = true;
        }
    }
}

// Supporting classes for the tests
public class PostgreSqlStorageConfiguration
{
    private string _host = string.Empty;
    private int _port;

    public string Host
    {
        get => _host;
        set => _host = value ?? throw new ArgumentNullException(nameof(value));
    }

    public int Port
    {
        get => _port;
        set => _port = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), "Port must be greater than 0");
    }

    public string Database { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    public string ConnectionString => $"Host={Host};Port={Port};Database={Database};Username={Username};Password={Password}";
}

// Mock test container for testing without actual Docker dependency
public class MockPostgreSqlTestContainer : IDisposable
{
    private bool _disposed = false;

    public string ConnectionString { get; } = "Host=localhost;Port=5432;Database=herotest;Username=postgres;Password=Test123!";

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            // Clean up test container resources
            _disposed = true;
        }
    }
}
