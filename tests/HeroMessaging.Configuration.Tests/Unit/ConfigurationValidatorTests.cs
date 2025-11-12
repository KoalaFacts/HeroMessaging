using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeroMessaging.Configuration.Tests.Unit
{
    [Trait("Category", "Unit")]
    public sealed class ConfigurationValidatorTests
    {
    [Fact]
    public void Constructor_WithNullServices_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new ConfigurationValidator(null!));
        Assert.Equal("services", ex.ParamName);
    }

    [Fact]
    public void Constructor_WithValidServices_CreatesValidator()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var validator = new ConfigurationValidator(services);

        // Assert
        Assert.NotNull(validator);
    }

    [Fact]
    public void Constructor_WithLogger_CreatesValidator()
    {
        // Arrange
        var services = new ServiceCollection();
        var logger = new Mock<ILogger<ConfigurationValidator>>().Object;

        // Act
        var validator = new ConfigurationValidator(services, logger);

        // Assert
        Assert.NotNull(validator);
    }

    [Fact]
    public void Validate_WithoutHeroMessaging_ReturnsErrorResult()
    {
        // Arrange
        var services = new ServiceCollection();
        var validator = new ConfigurationValidator(services);

        // Act
        var report = validator.Validate();

        // Assert
        Assert.False(report.IsValid);
        Assert.False(report.IsValid);
    }

    [Fact]
    public void Validate_WithHeroMessaging_ReturnsValidResult()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IHeroMessaging>(new Mock<IHeroMessaging>().Object);
        var validator = new ConfigurationValidator(services);

        // Act
        var report = validator.Validate();

        // Assert
        Assert.True(report.IsValid);
    }

    [Fact(Skip = "Requires actual processor types to be implemented")]
    public void Validate_WithOutboxProcessorButNoStorage_ReturnsError()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IHeroMessaging>(new Mock<IHeroMessaging>().Object);
        // TODO: Register actual OutboxProcessor when it's implemented
        var validator = new ConfigurationValidator(services);

        // Act
        var report = validator.Validate() as ValidationReport;

        // Assert
        Assert.NotNull(report);
        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, e => e.Message.Contains("IOutboxStorage"));
    }

    [Fact(Skip = "Requires actual processor types to be implemented")]
    public void Validate_WithInboxProcessorButNoStorage_ReturnsError()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IHeroMessaging>(new Mock<IHeroMessaging>().Object);
        // TODO: Register actual InboxProcessor when it's implemented
        var validator = new ConfigurationValidator(services);

        // Act
        var report = validator.Validate() as ValidationReport;

        // Assert
        Assert.NotNull(report);
        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, e => e.Message.Contains("IInboxStorage"));
    }

    [Fact(Skip = "Requires actual processor types to be implemented")]
    public void Validate_WithQueueProcessorButNoStorage_ReturnsWarning()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IHeroMessaging>(new Mock<IHeroMessaging>().Object);
        // TODO: Register actual QueueProcessor when it's implemented
        var validator = new ConfigurationValidator(services);

        // Act
        var report = validator.Validate() as ValidationReport;

        // Assert
        Assert.NotNull(report);
        Assert.True(report.IsValid); // Warnings don't invalidate
        Assert.True(report.HasWarnings);
        Assert.Contains(report.Warnings, w => w.Message.Contains("IQueueStorage"));
    }

    [Fact(Skip = "Requires actual processor types to be implemented")]
    public void Validate_WithOutboxAndStorage_ReturnsValid()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IHeroMessaging>(new Mock<IHeroMessaging>().Object);
        // TODO: Register actual OutboxProcessor when it's implemented
        services.AddSingleton<IOutboxStorage>(new Mock<IOutboxStorage>().Object);
        var validator = new ConfigurationValidator(services);

        // Act
        var report = validator.Validate();

        // Assert
        Assert.True(report.IsValid);
    }

    [Fact(Skip = "Requires actual processor types to be implemented")]
    public void Validate_WithProcessorsButNoSerializer_ReturnsWarning()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IHeroMessaging>(new Mock<IHeroMessaging>().Object);
        // TODO: Register actual OutboxProcessor when it's implemented
        services.AddSingleton<IOutboxStorage>(new Mock<IOutboxStorage>().Object);
        var validator = new ConfigurationValidator(services);

        // Act
        var report = validator.Validate() as ValidationReport;

        // Assert
        Assert.NotNull(report);
        Assert.True(report.IsValid);
        Assert.True(report.HasWarnings);
        Assert.Contains(report.Warnings, w => w.Message.Contains("IMessageSerializer"));
    }

    [Fact]
    public void Validate_WithMultipleImplementations_ReturnsWarning()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IHeroMessaging>(new Mock<IHeroMessaging>().Object);
        services.AddSingleton<IOutboxStorage>(new Mock<IOutboxStorage>().Object);
        services.AddSingleton<IOutboxStorage>(new Mock<IOutboxStorage>().Object); // Duplicate
        var validator = new ConfigurationValidator(services);

        // Act
        var report = validator.Validate();

        // Assert
        Assert.True(report.IsValid);
        Assert.True(report.HasWarnings);
        Assert.True(report.HasWarnings);
    }

    [Fact]
    public void ValidationResult_Construction_SetsProperties()
    {
        // Arrange
        var severity = ValidationSeverity.Error;
        var message = "Test error";

        // Act
        var result = new ValidationResult(severity, message);

        // Assert
        Assert.Equal(severity, result.Severity);
        Assert.Equal(message, result.Message);
    }

    [Fact]
    public void ValidationResult_WithNullMessage_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new ValidationResult(ValidationSeverity.Error, null!));
        Assert.Equal("message", ex.ParamName);
    }

    [Fact]
    public void ValidationReport_WithNoErrors_IsValid()
    {
        // Arrange
        var results = new[]
        {
            new ValidationResult(ValidationSeverity.Info, "Info message"),
            new ValidationResult(ValidationSeverity.Warning, "Warning message")
        };

        // Act
        var report = new ValidationReport(results);

        // Assert
        Assert.True(report.IsValid);
        Assert.True(report.HasWarnings);
        Assert.Empty(report.Errors);
        Assert.Single(report.Warnings);
        Assert.Single(report.Information);
    }

    [Fact]
    public void ValidationReport_WithErrors_IsNotValid()
    {
        // Arrange
        var results = new[]
        {
            new ValidationResult(ValidationSeverity.Error, "Error message"),
            new ValidationResult(ValidationSeverity.Warning, "Warning message")
        };

        // Act
        var report = new ValidationReport(results);

        // Assert
        Assert.False(report.IsValid);
        Assert.True(report.HasWarnings);
        Assert.Single(report.Errors);
        Assert.Single(report.Warnings);
    }

    [Fact]
    public void ValidationReport_WithNullResults_CreatesEmptyReport()
    {
        // Act
        var report = new ValidationReport(null);

        // Assert
        Assert.True(report.IsValid);
        Assert.False(report.HasWarnings);
        Assert.Empty(report.Results);
    }

    [Fact]
    public void ValidationReport_ToString_WithNoIssues_ReturnsValidMessage()
    {
        // Arrange
        var report = new ValidationReport(Array.Empty<ValidationResult>());

        // Act
        var message = report.ToString();

        // Assert
        Assert.Equal("Configuration is valid", message);
    }

    [Fact]
    public void ValidationReport_ToString_WithIssues_ReturnsSummary()
    {
        // Arrange
        var results = new[]
        {
            new ValidationResult(ValidationSeverity.Error, "Error 1"),
            new ValidationResult(ValidationSeverity.Error, "Error 2"),
            new ValidationResult(ValidationSeverity.Warning, "Warning 1")
        };
        var report = new ValidationReport(results);

        // Act
        var message = report.ToString();

        // Assert
        Assert.Contains("2 error(s)", message);
        Assert.Contains("1 warning(s)", message);
    }

    [Fact]
    public void ValidationSeverity_HasExpectedValues()
    {
        // Assert
        Assert.Equal(0, (int)ValidationSeverity.Info);
        Assert.Equal(1, (int)ValidationSeverity.Warning);
        Assert.Equal(2, (int)ValidationSeverity.Error);
    }

    [Fact]
    public void Validate_CanBeCalledMultipleTimes()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IHeroMessaging>(new Mock<IHeroMessaging>().Object);
        var validator = new ConfigurationValidator(services);

        // Act
        var report1 = validator.Validate();
        var report2 = validator.Validate();

        // Assert
        Assert.True(report1.IsValid);
        Assert.True(report2.IsValid);
        Assert.NotSame(report1, report2); // Different instances
    }
    }
}
