using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Configuration;
using Moq;
using Xunit;

namespace HeroMessaging.Configuration.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class ExtensionsToIStorageBuilderTests
{
    [Fact]
    public void UseSqlServer_WithConnectionString_ThrowsNotImplementedException()
    {
        // Arrange
        var mockBuilder = new Mock<IStorageBuilder>();
        var connectionString = "Server=localhost;Database=test;";

        // Act & Assert
        var exception = Assert.Throws<NotImplementedException>(() =>
            mockBuilder.Object.UseSqlServer(connectionString));

        Assert.Contains("SQL Server storage is not yet implemented", exception.Message);
    }

    [Fact]
    public void UseSqlServer_WithConnectionStringAndOptions_ThrowsNotImplementedException()
    {
        // Arrange
        var mockBuilder = new Mock<IStorageBuilder>();
        var connectionString = "Server=localhost;Database=test;";

        // Act & Assert
        var exception = Assert.Throws<NotImplementedException>(() =>
            mockBuilder.Object.UseSqlServer(connectionString, options => { }));

        Assert.Contains("SQL Server storage is not yet implemented", exception.Message);
    }

    [Fact]
    public void UsePostgreSql_WithConnectionString_ThrowsNotImplementedException()
    {
        // Arrange
        var mockBuilder = new Mock<IStorageBuilder>();
        var connectionString = "Host=localhost;Database=test;";

        // Act & Assert
        var exception = Assert.Throws<NotImplementedException>(() =>
            mockBuilder.Object.UsePostgreSql(connectionString));

        Assert.Contains("PostgreSQL storage is not yet implemented", exception.Message);
    }

    [Fact]
    public void UsePostgreSql_WithConnectionStringAndOptions_ThrowsNotImplementedException()
    {
        // Arrange
        var mockBuilder = new Mock<IStorageBuilder>();
        var connectionString = "Host=localhost;Database=test;";

        // Act & Assert
        var exception = Assert.Throws<NotImplementedException>(() =>
            mockBuilder.Object.UsePostgreSql(connectionString, options => { }));

        Assert.Contains("PostgreSQL storage is not yet implemented", exception.Message);
    }
}
