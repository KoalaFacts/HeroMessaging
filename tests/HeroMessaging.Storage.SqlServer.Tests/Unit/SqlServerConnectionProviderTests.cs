using HeroMessaging.Storage.SqlServer;
using Microsoft.Data.SqlClient;
using Moq;
using Xunit;

namespace HeroMessaging.Storage.SqlServer.Tests.Unit;

public class SqlServerConnectionProviderTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullConnectionString_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new SqlServerConnectionProvider(null!));

        Assert.Equal("connectionString", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithValidConnectionString_CreatesInstance()
    {
        // Arrange
        var connectionString = "Server=localhost;Database=test;User=test;Password=test";

        // Act
        var provider = new SqlServerConnectionProvider(connectionString);

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
        var mockConnection = new Mock<SqlConnection>("Server=localhost;Database=test");
        var mockTransaction = new Mock<SqlTransaction>();

        // Act
        var provider = new SqlServerConnectionProvider(mockConnection.Object, mockTransaction.Object);

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
            new SqlServerConnectionProvider(null!, null));

        Assert.Equal("connection", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithSharedConnectionAndNoTransaction_CreatesInstance()
    {
        // Arrange
        var mockConnection = new Mock<SqlConnection>("Server=localhost;Database=test");

        // Act
        var provider = new SqlServerConnectionProvider(mockConnection.Object, null);

        // Assert
        Assert.NotNull(provider);
        Assert.True(provider.IsSharedConnection);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetTransaction_WithSharedTransaction_ReturnsTransaction()
    {
        // Arrange
        var mockConnection = new Mock<SqlConnection>("Server=localhost;Database=test");
        var mockTransaction = new Mock<SqlTransaction>();
        var provider = new SqlServerConnectionProvider(mockConnection.Object, mockTransaction.Object);

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
        var connectionString = "Server=localhost;Database=test;User=test;Password=test";
        var provider = new SqlServerConnectionProvider(connectionString);

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
        var connectionString = "Server=localhost;Database=test;User=test;Password=test";
        var provider = new SqlServerConnectionProvider(connectionString);

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
        var mockConnection = new Mock<SqlConnection>("Server=localhost;Database=test");
        var provider = new SqlServerConnectionProvider(mockConnection.Object, null);

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
        var connectionString = "Server=localhost;Database=test;User=test;Password=test";
        var provider = new SqlServerConnectionProvider(connectionString);

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
        var connectionString = "Server=localhost;Database=test;User=test;Password=test";
        var mockConnection = new Mock<SqlConnection>(connectionString);
        mockConnection.Setup(c => c.ConnectionString).Returns(connectionString);
        var provider = new SqlServerConnectionProvider(mockConnection.Object, null);

        // Act
        var result = provider.ConnectionString;

        // Assert
        Assert.Equal(connectionString, result);
    }
}
