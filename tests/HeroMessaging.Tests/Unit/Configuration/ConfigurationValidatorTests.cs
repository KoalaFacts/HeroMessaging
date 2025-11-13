using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Configuration;

[Trait("Category", "Unit")]
public sealed class ConfigurationValidatorTests
{
    private readonly ServiceCollection _services;
    private readonly Mock<ILogger<ConfigurationValidator>> _loggerMock;

    public ConfigurationValidatorTests()
    {
        _services = new ServiceCollection();
        _loggerMock = new Mock<ILogger<ConfigurationValidator>>();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidServices_CreatesInstance()
    {
        // Arrange & Act
        var validator = new ConfigurationValidator(_services);

        // Assert
        Assert.NotNull(validator);
    }

    [Fact]
    public void Constructor_WithNullServices_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new ConfigurationValidator(null!, _loggerMock.Object));

        Assert.Equal("services", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullLogger_CreatesInstance()
    {
        // Arrange & Act
        var validator = new ConfigurationValidator(_services, null);

        // Assert
        Assert.NotNull(validator);
    }

    #endregion

    #region Validate - IHeroMessaging Tests

    [Fact]
    public void Validate_WithoutIHeroMessaging_ReturnsErrorResult()
    {
        // Arrange
        var validator = new ConfigurationValidator(_services, _loggerMock.Object);

        // Act
        var report = validator.Validate();

        // Assert
        Assert.False(report.IsValid);
        Assert.NotEmpty(report.Errors);
        Assert.Contains(report.Errors, e => e.Message.Contains("IHeroMessaging is not registered"));
    }

    [Fact]
    public void Validate_WithIHeroMessaging_DoesNotReturnIHeroMessagingError()
    {
        // Arrange
        _services.AddSingleton<Abstractions.IHeroMessaging>(Mock.Of<Abstractions.IHeroMessaging>());
        var validator = new ConfigurationValidator(_services, _loggerMock.Object);

        // Act
        var report = validator.Validate();

        // Assert
        Assert.DoesNotContain(report.Errors, e => e.Message.Contains("IHeroMessaging is not registered"));
    }

    #endregion

    #region Validate - Storage Configuration Tests

    [Fact]
    public void Validate_WithOutboxProcessorButNoStorage_ReturnsErrorResult()
    {
        // Arrange
        _services.AddSingleton<Abstractions.IHeroMessaging>(Mock.Of<Abstractions.IHeroMessaging>());
        _services.AddSingleton<object>(new { Type = typeof(object).FullName = "HeroMessaging.Processing.OutboxProcessor" });

        // Add a service with the OutboxProcessor type name
        var descriptor = ServiceDescriptor.Singleton(
            Type.GetType("HeroMessaging.Processing.OutboxProcessor, HeroMessaging") ?? typeof(object),
            _ => new object());
        _services.Add(descriptor);

        var validator = new ConfigurationValidator(_services, _loggerMock.Object);

        // Act
        var report = validator.Validate();

        // Assert
        Assert.Contains(report.Errors, e => e.Message.Contains("IOutboxStorage is not configured"));
    }

    [Fact]
    public void Validate_WithOutboxProcessorAndStorage_DoesNotReturnStorageError()
    {
        // Arrange
        _services.AddSingleton<Abstractions.IHeroMessaging>(Mock.Of<Abstractions.IHeroMessaging>());
        _services.AddSingleton<IOutboxStorage>(Mock.Of<IOutboxStorage>());
        var descriptor = ServiceDescriptor.Singleton(
            Type.GetType("HeroMessaging.Processing.OutboxProcessor, HeroMessaging") ?? typeof(object),
            _ => new object());
        _services.Add(descriptor);

        var validator = new ConfigurationValidator(_services, _loggerMock.Object);

        // Act
        var report = validator.Validate();

        // Assert
        Assert.DoesNotContain(report.Errors, e => e.Message.Contains("IOutboxStorage is not configured"));
    }

    [Fact]
    public void Validate_WithInboxProcessorButNoStorage_ReturnsErrorResult()
    {
        // Arrange
        _services.AddSingleton<Abstractions.IHeroMessaging>(Mock.Of<Abstractions.IHeroMessaging>());
        var descriptor = ServiceDescriptor.Singleton(
            Type.GetType("HeroMessaging.Processing.InboxProcessor, HeroMessaging") ?? typeof(object),
            _ => new object());
        _services.Add(descriptor);

        var validator = new ConfigurationValidator(_services, _loggerMock.Object);

        // Act
        var report = validator.Validate();

        // Assert
        Assert.Contains(report.Errors, e => e.Message.Contains("IInboxStorage is not configured"));
    }

    [Fact]
    public void Validate_WithInboxProcessorAndStorage_DoesNotReturnStorageError()
    {
        // Arrange
        _services.AddSingleton<Abstractions.IHeroMessaging>(Mock.Of<Abstractions.IHeroMessaging>());
        _services.AddSingleton<IInboxStorage>(Mock.Of<IInboxStorage>());
        var descriptor = ServiceDescriptor.Singleton(
            Type.GetType("HeroMessaging.Processing.InboxProcessor, HeroMessaging") ?? typeof(object),
            _ => new object());
        _services.Add(descriptor);

        var validator = new ConfigurationValidator(_services, _loggerMock.Object);

        // Act
        var report = validator.Validate();

        // Assert
        Assert.DoesNotContain(report.Errors, e => e.Message.Contains("IInboxStorage is not configured"));
    }

    [Fact]
    public void Validate_WithQueueProcessorButNoStorage_ReturnsWarningResult()
    {
        // Arrange
        _services.AddSingleton<Abstractions.IHeroMessaging>(Mock.Of<Abstractions.IHeroMessaging>());
        var descriptor = ServiceDescriptor.Singleton(
            Type.GetType("HeroMessaging.Processing.QueueProcessor, HeroMessaging") ?? typeof(object),
            _ => new object());
        _services.Add(descriptor);

        var validator = new ConfigurationValidator(_services, _loggerMock.Object);

        // Act
        var report = validator.Validate();

        // Assert
        Assert.True(report.HasWarnings);
        Assert.Contains(report.Warnings, w => w.Message.Contains("IQueueStorage is not configured"));
    }

    #endregion

    #region Validate - Serialization Configuration Tests

    [Fact]
    public void Validate_WithPersistenceButNoSerializer_ReturnsWarningResult()
    {
        // Arrange
        _services.AddSingleton<Abstractions.IHeroMessaging>(Mock.Of<Abstractions.IHeroMessaging>());
        _services.AddSingleton<IOutboxStorage>(Mock.Of<IOutboxStorage>());
        var descriptor = ServiceDescriptor.Singleton(
            Type.GetType("HeroMessaging.Processing.OutboxProcessor, HeroMessaging") ?? typeof(object),
            _ => new object());
        _services.Add(descriptor);

        var validator = new ConfigurationValidator(_services, _loggerMock.Object);

        // Act
        var report = validator.Validate();

        // Assert
        Assert.True(report.HasWarnings);
        Assert.Contains(report.Warnings, w => w.Message.Contains("IMessageSerializer is not configured"));
    }

    [Fact]
    public void Validate_WithPersistenceAndSerializer_DoesNotReturnSerializerWarning()
    {
        // Arrange
        _services.AddSingleton<Abstractions.IHeroMessaging>(Mock.Of<Abstractions.IHeroMessaging>());
        _services.AddSingleton<IOutboxStorage>(Mock.Of<IOutboxStorage>());
        _services.AddSingleton<IMessageSerializer>(Mock.Of<IMessageSerializer>());
        var descriptor = ServiceDescriptor.Singleton(
            Type.GetType("HeroMessaging.Processing.OutboxProcessor, HeroMessaging") ?? typeof(object),
            _ => new object());
        _services.Add(descriptor);

        var validator = new ConfigurationValidator(_services, _loggerMock.Object);

        // Act
        var report = validator.Validate();

        // Assert
        Assert.DoesNotContain(report.Warnings, w => w.Message.Contains("IMessageSerializer is not configured"));
    }

    #endregion

    #region Validate - Configuration Consistency Tests

    [Fact]
    public void Validate_WithMultipleImplementationsOfSameInterface_ReturnsWarning()
    {
        // Arrange
        _services.AddSingleton<Abstractions.IHeroMessaging>(Mock.Of<Abstractions.IHeroMessaging>());
        _services.AddSingleton<IOutboxStorage>(Mock.Of<IOutboxStorage>());
        _services.AddSingleton<IOutboxStorage>(Mock.Of<IOutboxStorage>());

        var validator = new ConfigurationValidator(_services, _loggerMock.Object);

        // Act
        var report = validator.Validate();

        // Assert
        Assert.True(report.HasWarnings);
        Assert.Contains(report.Warnings, w => w.Message.Contains("Multiple implementations registered"));
    }

    [Fact]
    public void Validate_WithValidConfiguration_ReturnsValidReport()
    {
        // Arrange
        _services.AddSingleton<Abstractions.IHeroMessaging>(Mock.Of<Abstractions.IHeroMessaging>());

        var validator = new ConfigurationValidator(_services, _loggerMock.Object);

        // Act
        var report = validator.Validate();

        // Assert
        Assert.True(report.IsValid);
    }

    #endregion

    #region ValidationResult Tests

    [Fact]
    public void ValidationResult_Constructor_SetsProperties()
    {
        // Arrange & Act
        var result = new ValidationResult(ValidationSeverity.Error, "Test message");

        // Assert
        Assert.Equal(ValidationSeverity.Error, result.Severity);
        Assert.Equal("Test message", result.Message);
    }

    [Fact]
    public void ValidationResult_Constructor_WithNullMessage_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new ValidationResult(ValidationSeverity.Error, null!));

        Assert.Equal("message", exception.ParamName);
    }

    #endregion

    #region ValidationReport Tests

    [Fact]
    public void ValidationReport_WithNoErrors_IsValid()
    {
        // Arrange
        var results = new List<ValidationResult>
        {
            new ValidationResult(ValidationSeverity.Info, "Info message"),
            new ValidationResult(ValidationSeverity.Warning, "Warning message")
        };

        // Act
        var report = new ValidationReport(results);

        // Assert
        Assert.True(report.IsValid);
        Assert.True(report.HasWarnings);
        Assert.Single(report.Warnings);
        Assert.Single(report.Information);
        Assert.Empty(report.Errors);
    }

    [Fact]
    public void ValidationReport_WithErrors_IsNotValid()
    {
        // Arrange
        var results = new List<ValidationResult>
        {
            new ValidationResult(ValidationSeverity.Error, "Error message")
        };

        // Act
        var report = new ValidationReport(results);

        // Assert
        Assert.False(report.IsValid);
        Assert.Single(report.Errors);
    }

    [Fact]
    public void ValidationReport_WithNullResults_CreatesEmptyReport()
    {
        // Arrange & Act
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
        var report = new ValidationReport(new List<ValidationResult>());

        // Act
        var result = report.ToString();

        // Assert
        Assert.Equal("Configuration is valid", result);
    }

    [Fact]
    public void ValidationReport_ToString_WithIssues_ReturnsErrorAndWarningCount()
    {
        // Arrange
        var results = new List<ValidationResult>
        {
            new ValidationResult(ValidationSeverity.Error, "Error 1"),
            new ValidationResult(ValidationSeverity.Error, "Error 2"),
            new ValidationResult(ValidationSeverity.Warning, "Warning 1")
        };
        var report = new ValidationReport(results);

        // Act
        var result = report.ToString();

        // Assert
        Assert.Contains("2 error(s)", result);
        Assert.Contains("1 warning(s)", result);
    }

    [Fact]
    public void ValidationReport_Errors_ReturnsOnlyErrors()
    {
        // Arrange
        var results = new List<ValidationResult>
        {
            new ValidationResult(ValidationSeverity.Error, "Error message"),
            new ValidationResult(ValidationSeverity.Warning, "Warning message"),
            new ValidationResult(ValidationSeverity.Info, "Info message")
        };
        var report = new ValidationReport(results);

        // Act
        var errors = report.Errors.ToList();

        // Assert
        Assert.Single(errors);
        Assert.All(errors, e => Assert.Equal(ValidationSeverity.Error, e.Severity));
    }

    [Fact]
    public void ValidationReport_Warnings_ReturnsOnlyWarnings()
    {
        // Arrange
        var results = new List<ValidationResult>
        {
            new ValidationResult(ValidationSeverity.Error, "Error message"),
            new ValidationResult(ValidationSeverity.Warning, "Warning message"),
            new ValidationResult(ValidationSeverity.Info, "Info message")
        };
        var report = new ValidationReport(results);

        // Act
        var warnings = report.Warnings.ToList();

        // Assert
        Assert.Single(warnings);
        Assert.All(warnings, w => Assert.Equal(ValidationSeverity.Warning, w.Severity));
    }

    [Fact]
    public void ValidationReport_Information_ReturnsOnlyInfo()
    {
        // Arrange
        var results = new List<ValidationResult>
        {
            new ValidationResult(ValidationSeverity.Error, "Error message"),
            new ValidationResult(ValidationSeverity.Warning, "Warning message"),
            new ValidationResult(ValidationSeverity.Info, "Info message")
        };
        var report = new ValidationReport(results);

        // Act
        var information = report.Information.ToList();

        // Assert
        Assert.Single(information);
        Assert.All(information, i => Assert.Equal(ValidationSeverity.Info, i.Severity));
    }

    #endregion

    #region ValidationSeverity Enum Tests

    [Fact]
    public void ValidationSeverity_HasExpectedValues()
    {
        // Arrange & Act & Assert
        Assert.Equal(0, (int)ValidationSeverity.Info);
        Assert.Equal(1, (int)ValidationSeverity.Warning);
        Assert.Equal(2, (int)ValidationSeverity.Error);
    }

    #endregion
}
