using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Core.Validation;

namespace HeroMessaging.Tests.Unit.Validation;

public class RequiredFieldsValidatorTests
{
    private readonly RequiredFieldsValidator _validator = new();
    
    [Fact]
    public async Task ValidateAsync_WithAllRequiredFields_ShouldReturnSuccess()
    {
        // Arrange
        var message = new ValidMessage
        {
            MessageId = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            RequiredString = "Value",
            RequiredNumber = 42
        };
        
        // Act
        var result = await _validator.ValidateAsync(message);
        
        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }
    
    [Fact]
    public async Task ValidateAsync_WithEmptyMessageId_ShouldReturnFailure()
    {
        // Arrange
        var message = new ValidMessage
        {
            MessageId = Guid.Empty,
            Timestamp = DateTime.UtcNow,
            RequiredString = "Value"
        };
        
        // Act
        var result = await _validator.ValidateAsync(message);
        
        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("MessageId is required", result.Errors[0]);
    }
    
    [Fact]
    public async Task ValidateAsync_WithDefaultTimestamp_ShouldReturnFailure()
    {
        // Arrange
        var message = new ValidMessage
        {
            MessageId = Guid.NewGuid(),
            Timestamp = default,
            RequiredString = "Value"
        };
        
        // Act
        var result = await _validator.ValidateAsync(message);
        
        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Timestamp is required", result.Errors[0]);
    }
    
    [Fact]
    public async Task ValidateAsync_WithMissingRequiredAttribute_ShouldReturnFailure()
    {
        // Arrange
        var message = new ValidMessage
        {
            MessageId = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            RequiredString = null // Missing required field
        };
        
        // Act
        var result = await _validator.ValidateAsync(message);
        
        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("RequiredString", result.Errors[0]);
    }
    
    [Fact]
    public async Task ValidateAsync_WithEmptyRequiredString_ShouldReturnFailure()
    {
        // Arrange
        var message = new ValidMessage
        {
            MessageId = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            RequiredString = "   " // Whitespace only
        };
        
        // Act
        var result = await _validator.ValidateAsync(message);
        
        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("RequiredString", result.Errors[0]);
    }
    
    [Fact]
    public async Task ValidateAsync_WithOptionalFieldMissing_ShouldReturnSuccess()
    {
        // Arrange
        var message = new ValidMessage
        {
            MessageId = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            RequiredString = "Value",
            OptionalString = null // Optional field can be null
        };
        
        // Act
        var result = await _validator.ValidateAsync(message);
        
        // Assert
        Assert.True(result.IsValid);
    }
    
    private class ValidMessage : IMessage
    {
        public Guid MessageId { get; set; }
        public DateTime Timestamp { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
        public string? CorrelationId { get; set; }
        
        [Required]
        public string? RequiredString { get; set; }
        
        [Required]
        public int RequiredNumber { get; set; }
        
        public string? OptionalString { get; set; }
    }
}