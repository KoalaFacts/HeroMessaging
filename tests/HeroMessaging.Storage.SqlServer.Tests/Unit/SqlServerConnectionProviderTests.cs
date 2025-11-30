using Xunit;

namespace HeroMessaging.Storage.SqlServer.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class SqlServerConnectionProviderTests
{
    private const string ValidConnectionString = "Server=localhost;Database=test;User Id=user;Password=pass;TrustServerCertificate=true";

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidConnectionString_Succeeds()
    {
        // Act
        var provider = new SqlServerConnectionProvider(ValidConnectionString);

        // Assert
        Assert.NotNull(provider);
        Assert.Equal(ValidConnectionString, provider.ConnectionString);
        Assert.False(provider.IsSharedConnection);
    }

    [Fact]
    public void Constructor_WithNullConnectionString_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new SqlServerConnectionProvider(null!));
        Assert.Equal("connectionString", exception.ParamName);
    }

    #endregion

    #region Connection String Validation

    [Theory]
    [InlineData("Server=localhost")]
    [InlineData("Server=localhost;Database=test")]
    [InlineData("Server=localhost;Database=test;Integrated Security=true")]
    [InlineData("Server=localhost,1433;Database=test;User Id=sa;Password=Pass123!")]
    public void Constructor_WithValidConnectionStringFormats_Succeeds(string connectionString)
    {
        // Act
        var provider = new SqlServerConnectionProvider(connectionString);

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
        var provider = new SqlServerConnectionProvider(ValidConnectionString);

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
        var provider = new SqlServerConnectionProvider(ValidConnectionString);

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
        var provider = new SqlServerConnectionProvider(ValidConnectionString);

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
        var connectionString = "Server=localhost;Database=test;Pooling=true;Min Pool Size=5;Max Pool Size=100";

        // Act
        var provider = new SqlServerConnectionProvider(connectionString);

        // Assert
        Assert.Equal(connectionString, provider.ConnectionString);
    }

    [Fact]
    public void ConnectionString_PreservesTimeoutSettings()
    {
        // Arrange
        var connectionString = "Server=localhost;Database=test;Connection Timeout=60;Command Timeout=120";

        // Act
        var provider = new SqlServerConnectionProvider(connectionString);

        // Assert
        Assert.Equal(connectionString, provider.ConnectionString);
    }

    [Fact]
    public void ConnectionString_PreservesEncryptionSettings()
    {
        // Arrange
        var connectionString = "Server=localhost;Database=test;Encrypt=true;TrustServerCertificate=false";

        // Act
        var provider = new SqlServerConnectionProvider(connectionString);

        // Assert
        Assert.Equal(connectionString, provider.ConnectionString);
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public void IsSharedConnection_AccessedConcurrently_ReturnsConsistentValue()
    {
        // Arrange
        var provider = new SqlServerConnectionProvider(ValidConnectionString);

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
        var provider = new SqlServerConnectionProvider(ValidConnectionString);

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
        var provider = new SqlServerConnectionProvider(ValidConnectionString);

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
