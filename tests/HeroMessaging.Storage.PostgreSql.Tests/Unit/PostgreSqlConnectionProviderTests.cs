using HeroMessaging.Storage.PostgreSql;
using Moq;
using Npgsql;
using Xunit;

namespace HeroMessaging.Storage.PostgreSql.Tests.Unit;

public class PostgreSqlConnectionProviderTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullConnectionString_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new PostgreSqlConnectionProvider(null!));

        Assert.Equal("connectionString", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithValidConnectionString_CreatesInstance()
    {
        // Arrange
        var connectionString = "Host=localhost;Database=test;Username=test;Password=test";

        // Act
        var provider = new PostgreSqlConnectionProvider(connectionString);

        // Assert
        Assert.NotNull(provider);
        Assert.Equal(connectionString, provider.ConnectionString);
        Assert.False(provider.IsSharedConnection);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithSharedConnection_CreatesInstance()
    {
        // Arrange
        var mockConnection = new Mock<NpgsqlConnection>("Host=localhost;Database=test");
        var mockTransaction = new Mock<NpgsqlTransaction>();

        // Act
        var provider = new PostgreSqlConnectionProvider(mockConnection.Object, mockTransaction.Object);

        // Assert
        Assert.NotNull(provider);
        Assert.True(provider.IsSharedConnection);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithSharedConnectionNullConnection_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new PostgreSqlConnectionProvider(null!, null));

        Assert.Equal("connection", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithSharedConnectionAndNoTransaction_CreatesInstance()
    {
        // Arrange
        var mockConnection = new Mock<NpgsqlConnection>("Host=localhost;Database=test");

        // Act
        var provider = new PostgreSqlConnectionProvider(mockConnection.Object, null);

        // Assert
        Assert.NotNull(provider);
        Assert.True(provider.IsSharedConnection);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetTransaction_WithSharedTransaction_ReturnsTransaction()
    {
        // Arrange
        var mockConnection = new Mock<NpgsqlConnection>("Host=localhost;Database=test");
        var mockTransaction = new Mock<NpgsqlTransaction>();
        var provider = new PostgreSqlConnectionProvider(mockConnection.Object, mockTransaction.Object);

        // Act
        var transaction = provider.GetTransaction();

        // Assert
        Assert.NotNull(transaction);
        Assert.Equal(mockTransaction.Object, transaction);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetTransaction_WithNoTransaction_ReturnsNull()
    {
        // Arrange
        var connectionString = "Host=localhost;Database=test;Username=test;Password=test";
        var provider = new PostgreSqlConnectionProvider(connectionString);

        // Act
        var transaction = provider.GetTransaction();

        // Assert
        Assert.Null(transaction);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void IsSharedConnection_WithConnectionString_ReturnsFalse()
    {
        // Arrange
        var connectionString = "Host=localhost;Database=test;Username=test;Password=test";
        var provider = new PostgreSqlConnectionProvider(connectionString);

        // Act
        var isShared = provider.IsSharedConnection;

        // Assert
        Assert.False(isShared);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void IsSharedConnection_WithSharedConnection_ReturnsTrue()
    {
        // Arrange
        var mockConnection = new Mock<NpgsqlConnection>("Host=localhost;Database=test");
        var provider = new PostgreSqlConnectionProvider(mockConnection.Object, null);

        // Act
        var isShared = provider.IsSharedConnection;

        // Assert
        Assert.True(isShared);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ConnectionString_WithProvidedConnectionString_ReturnsConnectionString()
    {
        // Arrange
        var connectionString = "Host=localhost;Database=test;Username=test;Password=test";
        var provider = new PostgreSqlConnectionProvider(connectionString);

        // Act
        var result = provider.ConnectionString;

        // Assert
        Assert.Equal(connectionString, result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ConnectionString_WithSharedConnection_ReturnsConnectionString()
    {
        // Arrange
        var connectionString = "Host=localhost;Database=test;Username=test;Password=test";
        var mockConnection = new Mock<NpgsqlConnection>(connectionString);
        mockConnection.Setup(c => c.ConnectionString).Returns(connectionString);
        var provider = new PostgreSqlConnectionProvider(mockConnection.Object, null);

        // Act
        var result = provider.ConnectionString;

        // Assert
        Assert.Equal(connectionString, result);
    }
}
