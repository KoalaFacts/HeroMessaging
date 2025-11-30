using Xunit;

namespace HeroMessaging.Storage.PostgreSql.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class PostgreSqlConnectionProviderTests
{
    private const string ValidConnectionString = "Host=localhost;Database=test;Username=user;Password=pass";

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidConnectionString_Succeeds()
    {
        // Act
        var provider = new PostgreSqlConnectionProvider(ValidConnectionString);

        // Assert
        Assert.NotNull(provider);
        Assert.Equal(ValidConnectionString, provider.ConnectionString);
        Assert.False(provider.IsSharedConnection);
    }

    [Fact]
    public void Constructor_WithNullConnectionString_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new PostgreSqlConnectionProvider(null!));
        Assert.Equal("connectionString", exception.ParamName);
    }

    #endregion

    #region Connection String Validation

    [Theory]
    [InlineData("Host=localhost")]
    [InlineData("Host=localhost;Database=test")]
    [InlineData("Host=localhost;Database=test;Username=user;Password=pass;Port=5432")]
    public void Constructor_WithValidConnectionStringFormats_Succeeds(string connectionString)
    {
        // Act
        var provider = new PostgreSqlConnectionProvider(connectionString);

        // Assert
        Assert.NotNull(provider);
        Assert.Equal(connectionString, provider.ConnectionString);
    }

    #endregion

    #region GetTransaction Tests

    [Fact]
    public void GetTransaction_WithNoTransaction_ReturnsNull()
    {
        // Arrange
        var provider = new PostgreSqlConnectionProvider(ValidConnectionString);

        // Act
        var transaction = provider.GetTransaction();

        // Assert
        Assert.Null(transaction);
    }

    #endregion

    #region IsSharedConnection Tests

    [Fact]
    public void IsSharedConnection_WithConnectionString_ReturnsFalse()
    {
        // Arrange
        var provider = new PostgreSqlConnectionProvider(ValidConnectionString);

        // Act
        var isShared = provider.IsSharedConnection;

        // Assert
        Assert.False(isShared);
    }

    #endregion

    #region ConnectionString Property Tests

    [Fact]
    public void ConnectionString_WithConnectionStringConstructor_ReturnsOriginalString()
    {
        // Arrange
        var provider = new PostgreSqlConnectionProvider(ValidConnectionString);

        // Act
        var connectionString = provider.ConnectionString;

        // Assert
        Assert.Equal(ValidConnectionString, connectionString);
    }

    #endregion

    #region Connection Pooling Behavior Tests

    [Fact]
    public void ConnectionString_PreservesPoolingSettings()
    {
        // Arrange
        var connectionString = "Host=localhost;Database=test;Pooling=true;Minimum Pool Size=5;Maximum Pool Size=100";

        // Act
        var provider = new PostgreSqlConnectionProvider(connectionString);

        // Assert
        Assert.Equal(connectionString, provider.ConnectionString);
    }

    [Fact]
    public void ConnectionString_PreservesTimeoutSettings()
    {
        // Arrange
        var connectionString = "Host=localhost;Database=test;Timeout=60;Command Timeout=120";

        // Act
        var provider = new PostgreSqlConnectionProvider(connectionString);

        // Assert
        Assert.Equal(connectionString, provider.ConnectionString);
    }

    [Fact]
    public void ConnectionString_PreservesSslSettings()
    {
        // Arrange
        var connectionString = "Host=localhost;Database=test;SSL Mode=Require;Trust Server Certificate=true";

        // Act
        var provider = new PostgreSqlConnectionProvider(connectionString);

        // Assert
        Assert.Equal(connectionString, provider.ConnectionString);
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public void IsSharedConnection_AccessedConcurrently_ReturnsConsistentValue()
    {
        // Arrange
        var provider = new PostgreSqlConnectionProvider(ValidConnectionString);

        // Act
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() => provider.IsSharedConnection))
            .ToArray();

        Task.WaitAll(tasks);
        var results = tasks.Select(t => t.Result).ToArray();

        // Assert
        Assert.All(results, Assert.False);
    }

    [Fact]
    public void ConnectionString_AccessedConcurrently_ReturnsConsistentValue()
    {
        // Arrange
        var provider = new PostgreSqlConnectionProvider(ValidConnectionString);

        // Act
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() => provider.ConnectionString))
            .ToArray();

        Task.WaitAll(tasks);
        var results = tasks.Select(t => t.Result).ToArray();

        // Assert
        Assert.All(results, result => Assert.Equal(ValidConnectionString, result));
    }

    [Fact]
    public void GetTransaction_AccessedConcurrently_ReturnsConsistentNull()
    {
        // Arrange
        var provider = new PostgreSqlConnectionProvider(ValidConnectionString);

        // Act
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() => provider.GetTransaction()))
            .ToArray();

        Task.WaitAll(tasks);
        var results = tasks.Select(t => t.Result).ToArray();

        // Assert
        Assert.All(results, Assert.Null);
    }

    #endregion
}
