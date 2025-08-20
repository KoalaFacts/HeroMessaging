using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Core.Validation;
using System.Text;

namespace HeroMessaging.Tests.Unit.Validation;

public class MessageSizeValidatorTests
{
    [Fact]
    public async Task ValidateAsync_WithSmallMessage_ShouldReturnSuccess()
    {
        // Arrange
        var validator = new MessageSizeValidator(1024 * 1024); // 1MB limit
        var message = new TestMessage { Content = "Small message" };
        
        // Act
        var result = await validator.ValidateAsync(message);
        
        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }
    
    [Fact]
    public async Task ValidateAsync_WithLargeMessage_ShouldReturnFailure()
    {
        // Arrange
        var validator = new MessageSizeValidator(100); // 100 bytes limit
        var largeContent = new string('X', 1000); // Create large content
        var message = new TestMessage { Content = largeContent };
        
        // Act
        var result = await validator.ValidateAsync(message);
        
        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
        Assert.Contains("exceeds maximum allowed size", result.Errors[0]);
    }
    
    [Fact]
    public async Task ValidateAsync_WithExactSizeLimit_ShouldReturnSuccess()
    {
        // Arrange
        var validator = new MessageSizeValidator(200);
        var message = new TestMessage { Content = "Test" };
        
        // Act
        var result = await validator.ValidateAsync(message);
        
        // Assert
        Assert.True(result.IsValid);
    }
    
    private class TestMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public Dictionary<string, object>? Metadata { get; set; }
        public string? CorrelationId { get; set; }
        public string Content { get; set; } = string.Empty;
    }
}