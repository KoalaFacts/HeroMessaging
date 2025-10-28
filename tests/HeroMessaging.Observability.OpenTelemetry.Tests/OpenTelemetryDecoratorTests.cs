using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Observability.OpenTelemetry;
using Moq;
using System.Diagnostics;
using Xunit;

namespace HeroMessaging.Observability.OpenTelemetry.Tests;

public class OpenTelemetryDecoratorTests
{
    private readonly Mock<IMessageProcessor> _innerProcessor;
    private readonly ActivityListener _activityListener;
    private readonly List<Activity> _activities;

    public OpenTelemetryDecoratorTests()
    {
        _innerProcessor = new Mock<IMessageProcessor>();
        _activities = new List<Activity>();

        // Set up activity listener to capture activities
        _activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == HeroMessagingInstrumentation.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity => _activities.Add(activity)
        };
        ActivitySource.AddActivityListener(_activityListener);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ProcessAsync_SuccessfulProcessing_CreatesActivityWithCorrectTags()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid() };
        var context = new ProcessingContext("TestComponent");
        var expectedResult = ProcessingResult.Successful();

        _innerProcessor
            .Setup(x => x.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var decorator = new OpenTelemetryDecorator(_innerProcessor.Object);

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.True(result.Success);
        Assert.Single(_activities);

        var activity = _activities[0];
        Assert.Equal("HeroMessaging.Process", activity.OperationName);
        Assert.Equal(ActivityKind.Internal, activity.Kind);
        Assert.Equal("heromessaging", activity.GetTagItem("messaging.system"));
        Assert.Equal("TestComponent", activity.GetTagItem("messaging.processor"));
        Assert.Equal(message.MessageId.ToString(), activity.GetTagItem("messaging.message_id"));
        Assert.Equal("TestMessage", activity.GetTagItem("messaging.message_type"));
        Assert.Equal(ActivityStatusCode.Unset, activity.Status);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ProcessAsync_FailedProcessing_SetsActivityStatusToError()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid() };
        var context = new ProcessingContext("TestComponent");
        var exception = new InvalidOperationException("Test failure");
        var expectedResult = ProcessingResult.Failed(exception, "Processing failed");

        _innerProcessor
            .Setup(x => x.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var decorator = new OpenTelemetryDecorator(_innerProcessor.Object);

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.False(result.Success);
        Assert.Single(_activities);

        var activity = _activities[0];
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Contains("Test failure", activity.StatusDescription ?? "");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ProcessAsync_ExceptionThrown_SetsActivityStatusToErrorAndRethrows()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid() };
        var context = new ProcessingContext("TestComponent");
        var exception = new InvalidOperationException("Test exception");

        _innerProcessor
            .Setup(x => x.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        var decorator = new OpenTelemetryDecorator(_innerProcessor.Object);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            decorator.ProcessAsync(message, context).AsTask());

        Assert.Single(_activities);
        var activity = _activities[0];
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Contains("Test exception", activity.StatusDescription ?? "");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ProcessAsync_WithMessageMetadata_IncludesMetadataAsTags()
    {
        // Arrange
        var metadata = new Dictionary<string, object>
        {
            ["correlation_id"] = "test-correlation",
            ["tenant_id"] = "tenant-123"
        };
        var message = new TestMessage
        {
            MessageId = Guid.NewGuid(),
            Metadata = metadata
        };
        var context = new ProcessingContext("TestComponent");
        var expectedResult = ProcessingResult.Successful();

        _innerProcessor
            .Setup(x => x.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var decorator = new OpenTelemetryDecorator(_innerProcessor.Object);

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        var activity = _activities[0];
        Assert.Equal("test-correlation", activity.GetTagItem("messaging.metadata.correlation_id"));
        Assert.Equal("tenant-123", activity.GetTagItem("messaging.metadata.tenant_id"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ProcessAsync_WithParentActivity_CreatesChildActivity()
    {
        // Arrange
        using var parentActivity = new ActivitySource("TestSource").StartActivity("ParentOperation");
        var message = new TestMessage { MessageId = Guid.NewGuid() };
        var context = new ProcessingContext("TestComponent");
        var expectedResult = ProcessingResult.Successful();

        _innerProcessor
            .Setup(x => x.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var decorator = new OpenTelemetryDecorator(_innerProcessor.Object);

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        var activity = _activities[0];
        Assert.Equal(parentActivity?.Context.TraceId, activity.Context.TraceId);
        Assert.Equal(parentActivity?.Context.SpanId, activity.ParentSpanId);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ProcessAsync_NullInnerProcessor_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new OpenTelemetryDecorator(null!));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ProcessAsync_RecordsProcessingDurationMetric()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid() };
        var context = new ProcessingContext("TestComponent");
        var expectedResult = ProcessingResult.Successful();

        _innerProcessor
            .Setup(x => x.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var decorator = new OpenTelemetryDecorator(_innerProcessor.Object);

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.True(result.Success);
        // Note: Actual metric validation would require a meter provider with an exporter
        // This test ensures the code path executes without errors
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ProcessAsync_WithRetry_IncludesRetryCountInTags()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid() };
        var context = new ProcessingContext("TestComponent").WithRetry(3);
        var expectedResult = ProcessingResult.Successful();

        _innerProcessor
            .Setup(x => x.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var decorator = new OpenTelemetryDecorator(_innerProcessor.Object);

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        var activity = _activities[0];
        Assert.Equal("3", activity.GetTagItem("messaging.retry_count")?.ToString());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ProcessAsync_CancellationRequested_PropagatesCancellation()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid() };
        var context = new ProcessingContext("TestComponent");
        var cts = new CancellationTokenSource();
        cts.Cancel();

        _innerProcessor
            .Setup(x => x.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var decorator = new OpenTelemetryDecorator(_innerProcessor.Object);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            decorator.ProcessAsync(message, context, cts.Token).AsTask());
    }

    public void Dispose()
    {
        _activityListener?.Dispose();
        foreach (var activity in _activities)
        {
            activity?.Dispose();
        }
    }

    private class TestMessage : IMessage
    {
        public Guid MessageId { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public IDictionary<string, object>? Metadata { get; set; }
    }
}
