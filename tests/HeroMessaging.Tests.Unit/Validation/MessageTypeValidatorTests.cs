using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Queries;
using HeroMessaging.Core.Validation;

namespace HeroMessaging.Tests.Unit.Validation;

public class MessageTypeValidatorTests
{
    [Fact]
    public async Task ValidateAsync_WithCommandMessage_ShouldReturnSuccess()
    {
        // Arrange
        var validator = new MessageTypeValidator(typeof(ICommand));
        var message = new TestCommand();
        
        // Act
        var result = await validator.ValidateAsync(message);
        
        // Assert
        Assert.True(result.IsValid);
    }
    
    [Fact]
    public async Task ValidateAsync_WithEventMessage_ShouldReturnSuccess()
    {
        // Arrange
        var validator = new MessageTypeValidator(typeof(IEvent));
        var message = new TestEvent();
        
        // Act
        var result = await validator.ValidateAsync(message);
        
        // Assert
        Assert.True(result.IsValid);
    }
    
    [Fact]
    public async Task ValidateAsync_WithQueryMessage_ShouldReturnSuccess()
    {
        // Arrange
        var validator = new MessageTypeValidator(typeof(IQuery<>));
        var message = new TestQuery();
        
        // Act
        var result = await validator.ValidateAsync(message);
        
        // Assert
        Assert.True(result.IsValid);
    }
    
    [Fact]
    public async Task ValidateAsync_WithMultipleAllowedTypes_ShouldReturnSuccess()
    {
        // Arrange
        var validator = new MessageTypeValidator(typeof(ICommand), typeof(IEvent));
        var command = new TestCommand();
        var @event = new TestEvent();
        
        // Act
        var commandResult = await validator.ValidateAsync(command);
        var eventResult = await validator.ValidateAsync(@event);
        
        // Assert
        Assert.True(commandResult.IsValid);
        Assert.True(eventResult.IsValid);
    }
    
    [Fact]
    public async Task ValidateAsync_WithDisallowedType_ShouldReturnFailure()
    {
        // Arrange
        var validator = new MessageTypeValidator(typeof(ICommand));
        var message = new TestEvent(); // Event not allowed
        
        // Act
        var result = await validator.ValidateAsync(message);
        
        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("does not implement any of the allowed interfaces", result.Errors[0]);
    }
    
    [Fact]
    public async Task ValidateAsync_WithPlainMessage_ShouldReturnFailure()
    {
        // Arrange
        var validator = new MessageTypeValidator(typeof(ICommand), typeof(IEvent));
        var message = new PlainMessage(); // Not a command or event
        
        // Act
        var result = await validator.ValidateAsync(message);
        
        // Assert
        Assert.False(result.IsValid);
    }
    
    [Fact]
    public async Task ValidateAsync_WithDefaultAllowedTypes_ShouldAcceptStandardTypes()
    {
        // Arrange
        var validator = new MessageTypeValidator(); // Uses default types (ICommand, IEvent)
        var command = new TestCommand();
        var @event = new TestEvent();
        
        // Act
        var commandResult = await validator.ValidateAsync(command);
        var eventResult = await validator.ValidateAsync(@event);
        
        // Assert
        Assert.True(commandResult.IsValid);
        Assert.True(eventResult.IsValid);
    }
    
    private class TestCommand : ICommand
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public Dictionary<string, object>? Metadata { get; set; }
        public string? CorrelationId { get; set; }
    }
    
    private class TestEvent : IEvent
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public Dictionary<string, object>? Metadata { get; set; }
        public string? CorrelationId { get; set; }
    }
    
    private class TestQuery : IQuery<string>
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public Dictionary<string, object>? Metadata { get; set; }
        public string? CorrelationId { get; set; }
    }
    
    private class PlainMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public Dictionary<string, object>? Metadata { get; set; }
        public string? CorrelationId { get; set; }
    }
}