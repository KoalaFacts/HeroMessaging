using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Tests.TestUtilities;

/// <summary>
/// Test utility for creating test messages
/// </summary>
#pragma warning disable CA1052 // Static holder types should be Static or NotInheritable
public static class TestMessageBuilder
#pragma warning restore CA1052 // Static holder types should be Static or NotInheritable
{
    public static TestMessage CreateValidMessage(
        string content = "Test message content",
        string? correlationId = null,
        string? causationId = null)
    {
        return new TestMessage(
            messageId: Guid.NewGuid(),
            timestamp: DateTime.UtcNow,
            correlationId: correlationId,
            causationId: causationId,
            content: content,
            metadata: new Dictionary<string, object>
            {
                ["TestCreated"] = DateTime.UtcNow,
                ["TestEnvironment"] = "Unit"
            }
        );
    }

    public static TestMessage CreateInvalidMessage()
    {
        return new TestMessage(
            messageId: Guid.Empty,
            timestamp: DateTime.MinValue,
            correlationId: null,
            causationId: null,
            content: null,
            metadata: new Dictionary<string, object>
            {
                ["Invalid"] = true
            }
        );
    }

    public static TestMessage CreateLargeMessage(int contentSize = 10000)
    {
        return new TestMessage(
            messageId: Guid.NewGuid(),
            timestamp: DateTime.UtcNow,
            correlationId: null,
            causationId: null,
            content: new string('x', contentSize),
            metadata: new Dictionary<string, object>
            {
                ["Size"] = contentSize,
                ["Type"] = "Large"
            }
        );
    }
}

/// <summary>
/// Test message implementation with content property for testing
/// </summary>
public class TestMessage : IMessage
{
    public TestMessage(
        Guid messageId,
        DateTime timestamp,
        string? correlationId,
        string? causationId,
        string? content,
        Dictionary<string, object>? metadata = null)
    {
        MessageId = messageId;
        Timestamp = timestamp;
        CorrelationId = correlationId;
        CausationId = causationId;
        Content = content;
        Metadata = metadata;
    }

    public Guid MessageId { get; }
    public DateTime Timestamp { get; }
    public string? CorrelationId { get; }
    public string? CausationId { get; }
    public Dictionary<string, object>? Metadata { get; }

    /// <summary>
    /// Content property for test scenarios - not part of core IMessage contract
    /// </summary>
    public string? Content { get; }
}

/// <summary>
/// Test utilities for accessing content from test messages
/// </summary>
public static class TestMessageExtensions
{
    /// <summary>
    /// Safely extracts content from a test message
    /// </summary>
    public static string? GetTestContent(this IMessage message)
    {
        return (message as TestMessage)?.Content;
    }

    /// <summary>
    /// Asserts that two messages have the same content (for test messages)
    /// </summary>
    public static void AssertSameContent(IMessage expected, IMessage actual)
    {
        var expectedContent = expected.GetTestContent();
        var actualContent = actual.GetTestContent();

        if (expectedContent != actualContent)
        {
            throw new Xunit.Sdk.XunitException($"Expected content: '{expectedContent}', Actual content: '{actualContent}'");
        }
    }
}

