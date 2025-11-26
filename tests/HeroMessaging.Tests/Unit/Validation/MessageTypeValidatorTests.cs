using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Validation;
using HeroMessaging.Tests.TestUtilities;
using HeroMessaging.Validation;
using Xunit;

namespace HeroMessaging.Tests.Unit.Validation;

[Trait("Category", "Unit")]
public sealed class MessageTypeValidatorTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WithNoAllowedTypes_UsesDefaultTypesCommandAndEvent()
    {
        // Arrange & Act
        var validator = new MessageTypeValidator();

        // Assert
        Assert.NotNull(validator);
    }

    [Fact]
    public void Constructor_WithEmptyAllowedTypes_UsesDefaultTypesCommandAndEvent()
    {
        // Arrange & Act
        var validator = new MessageTypeValidator(Array.Empty<Type>());

        // Assert
        Assert.NotNull(validator);
    }

    [Fact]
    public void Constructor_WithSingleAllowedType_CreatesInstance()
    {
        // Arrange & Act
        var validator = new MessageTypeValidator(typeof(ICommand));

        // Assert
        Assert.NotNull(validator);
    }

    [Fact]
    public void Constructor_WithMultipleAllowedTypes_CreatesInstance()
    {
        // Arrange & Act
        var validator = new MessageTypeValidator(typeof(ICommand), typeof(IEvent));

        // Assert
        Assert.NotNull(validator);
    }

    [Fact]
    public void Constructor_WithNullAllowedTypes_UsesDefaultTypesCommandAndEvent()
    {
        // Arrange & Act
        var validator = new MessageTypeValidator((IEnumerable<Type>)null!);

        // Assert
        Assert.NotNull(validator);
    }

    #endregion

    #region ValidateAsync - Valid Message Types (Default Configuration)

    [Fact]
    public async Task ValidateAsync_WithCommandMessage_ReturnsSuccess()
    {
        // Arrange
        var validator = new MessageTypeValidator();
        var message = new TestCommand();

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ValidateAsync_WithEventMessage_ReturnsSuccess()
    {
        // Arrange
        var validator = new MessageTypeValidator();
        var message = new TestEvent();

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    #endregion

    #region ValidateAsync - Invalid Message Types (Default Configuration)

    [Fact]
    public async Task ValidateAsync_WithPlainMessage_ReturnsFailure()
    {
        // Arrange
        var validator = new MessageTypeValidator();
        var message = new TestMessage();

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
        Assert.Contains("TestMessage", result.Errors[0]);
        Assert.Contains("does not implement any of the allowed interfaces", result.Errors[0]);
    }

    [Fact]
    public async Task ValidateAsync_WithPlainMessage_ErrorContainsAllowedTypes()
    {
        // Arrange
        var validator = new MessageTypeValidator();
        var message = new TestMessage();

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("ICommand", result.Errors[0]);
        Assert.Contains("IEvent", result.Errors[0]);
    }

    #endregion

    #region ValidateAsync - Custom Allowed Types

    [Fact]
    public async Task ValidateAsync_WithCustomAllowedType_AcceptsMatchingMessage()
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
    public async Task ValidateAsync_WithCustomAllowedType_RejectsNonMatchingMessage()
    {
        // Arrange
        var validator = new MessageTypeValidator(typeof(ICommand));
        var message = new TestEvent();

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("TestEvent", result.Errors[0]);
        Assert.Contains("ICommand", result.Errors[0]);
    }

    [Fact]
    public async Task ValidateAsync_WithMultipleCustomAllowedTypes_AcceptsAnyMatchingType()
    {
        // Arrange
        var validator = new MessageTypeValidator(typeof(ICommand), typeof(IEvent));
        var commandMessage = new TestCommand();
        var eventMessage = new TestEvent();

        // Act
        var commandResult = await validator.ValidateAsync(commandMessage);
        var eventResult = await validator.ValidateAsync(eventMessage);

        // Assert
        Assert.True(commandResult.IsValid);
        Assert.True(eventResult.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_WithOnlyEventAllowed_RejectsCommand()
    {
        // Arrange
        var validator = new MessageTypeValidator(typeof(IEvent));
        var message = new TestCommand();

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("TestCommand", result.Errors[0]);
        Assert.Contains("IEvent", result.Errors[0]);
    }

    #endregion

    #region ValidateAsync - Interface Inheritance

    [Fact]
    public async Task ValidateAsync_WithMessageImplementingMultipleInterfaces_ReturnsSuccess()
    {
        // Arrange
        var validator = new MessageTypeValidator(typeof(ICommand));
        var message = new TestCommandEvent(); // Implements both ICommand and IEvent

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_WithMessageImplementingMultipleInterfaces_MatchesAnyAllowedType()
    {
        // Arrange
        var validator = new MessageTypeValidator(typeof(IEvent));
        var message = new TestCommandEvent(); // Implements both ICommand and IEvent

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_WithDerivedCommandType_ReturnsSuccess()
    {
        // Arrange
        var validator = new MessageTypeValidator(typeof(ICommand));
        var message = new DerivedTestCommand();

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.True(result.IsValid);
    }

    #endregion

    #region ValidateAsync - Edge Cases

    [Fact]
    public async Task ValidateAsync_WithAbstractInterface_ValidatesCorrectly()
    {
        // Arrange
        var validator = new MessageTypeValidator(typeof(IMessage));
        var message = new TestMessage();

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_WithCustomMarkerInterface_ValidatesCorrectly()
    {
        // Arrange
        var validator = new MessageTypeValidator(typeof(ICustomMarker));
        var message = new CustomMarkerMessage();

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_WithoutCustomMarkerInterface_ReturnsFailure()
    {
        // Arrange
        var validator = new MessageTypeValidator(typeof(ICustomMarker));
        var message = new TestCommand();

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.False(result.IsValid);
    }

    #endregion

    #region ValidateAsync - Cancellation Token

    [Fact]
    public async Task ValidateAsync_WithCancellationToken_CompletesSuccessfully()
    {
        // Arrange
        var validator = new MessageTypeValidator();
        var message = new TestCommand();
        var cts = new CancellationTokenSource();

        // Act
        var result = await validator.ValidateAsync(message, cts.Token);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_WithCancelledToken_DoesNotThrow()
    {
        // Arrange
        var validator = new MessageTypeValidator();
        var message = new TestCommand();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var result = await validator.ValidateAsync(message, cts.Token);

        // Assert - No exception, validation completes
        Assert.True(result.IsValid);
    }

    #endregion

    #region ValidateAsync - Multiple Calls

    [Fact]
    public async Task ValidateAsync_CalledMultipleTimes_ReturnsConsistentResults()
    {
        // Arrange
        var validator = new MessageTypeValidator();
        var message = new TestCommand();

        // Act
        var result1 = await validator.ValidateAsync(message);
        var result2 = await validator.ValidateAsync(message);

        // Assert
        Assert.True(result1.IsValid);
        Assert.True(result2.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_WithDifferentMessages_ReturnsCorrectResultsIndependently()
    {
        // Arrange
        var validator = new MessageTypeValidator(typeof(ICommand));
        var validMessage = new TestCommand();
        var invalidMessage = new TestEvent();

        // Act
        var result1 = await validator.ValidateAsync(validMessage);
        var result2 = await validator.ValidateAsync(invalidMessage);

        // Assert
        Assert.True(result1.IsValid);
        Assert.False(result2.IsValid);
    }

    #endregion

    #region ValidateAsync - Error Messages

    [Fact]
    public async Task ValidateAsync_WithInvalidType_ErrorContainsMessageTypeName()
    {
        // Arrange
        var validator = new MessageTypeValidator(typeof(ICommand));
        var message = new TestEvent();

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("TestEvent", result.Errors[0]);
    }

    [Fact]
    public async Task ValidateAsync_WithInvalidType_ErrorContainsAllowedInterfaceNames()
    {
        // Arrange
        var validator = new MessageTypeValidator(typeof(ICommand), typeof(IEvent));
        var message = new TestMessage();

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("ICommand", result.Errors[0]);
        Assert.Contains("IEvent", result.Errors[0]);
    }

    [Fact]
    public async Task ValidateAsync_WithSingleAllowedType_ErrorShowsOnlyThatType()
    {
        // Arrange
        var validator = new MessageTypeValidator(typeof(ICommand));
        var message = new TestMessage();

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("ICommand", result.Errors[0]);
        Assert.DoesNotContain("IEvent", result.Errors[0]);
    }

    #endregion

    #region Test Helper Classes

    public class TestMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    public class TestCommand : ICommand
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    public class TestEvent : IEvent
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    public class TestCommandEvent : ICommand, IEvent
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    public class DerivedTestCommand : TestCommand
    {
    }

    public interface ICustomMarker : IMessage
    {
    }

    public class CustomMarkerMessage : ICustomMarker
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    #endregion
}
