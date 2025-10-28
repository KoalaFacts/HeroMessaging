using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Observability.OpenTelemetry;
using System.Diagnostics;
using Xunit;

namespace HeroMessaging.Observability.OpenTelemetry.Tests;

public class TraceContextPropagatorTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void Inject_WithActivity_AddsTraceParentHeader()
    {
        // Arrange
        using var activity = new Activity("test");
        activity.Start();
        var envelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 1, 2, 3 });

        // Act
        var result = TraceContextPropagator.Inject(envelope, activity);

        // Assert
        Assert.True(result.HasHeader(TraceContextPropagator.TraceParentHeaderName));
        var traceParent = result.GetHeader<string>(TraceContextPropagator.TraceParentHeaderName);
        Assert.NotNull(traceParent);
        Assert.StartsWith("00-", traceParent); // W3C version 00
        Assert.Contains(activity.TraceId.ToString(), traceParent);
        Assert.Contains(activity.SpanId.ToString(), traceParent);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Inject_WithActivityAndTraceState_AddsTraceStateHeader()
    {
        // Arrange
        using var activity = new Activity("test");
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.TraceStateString = "vendor1=value1,vendor2=value2";
        activity.Start();
        var envelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 1, 2, 3 });

        // Act
        var result = TraceContextPropagator.Inject(envelope, activity);

        // Assert
        Assert.True(result.HasHeader(TraceContextPropagator.TraceStateHeaderName));
        var traceState = result.GetHeader<string>(TraceContextPropagator.TraceStateHeaderName);
        Assert.Equal("vendor1=value1,vendor2=value2", traceState);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Inject_WithNullActivity_ReturnsOriginalEnvelope()
    {
        // Arrange
        var envelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 1, 2, 3 });

        // Act
        var result = TraceContextPropagator.Inject(envelope, null);

        // Assert
        Assert.Equal(envelope, result);
        Assert.False(result.HasHeader(TraceContextPropagator.TraceParentHeaderName));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Inject_WithCurrentActivity_UsesCurrentActivity()
    {
        // Arrange
        using var activity = new Activity("test");
        activity.Start();
        var envelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 1, 2, 3 });

        // Act
        var result = TraceContextPropagator.InjectCurrent(envelope);

        // Assert
        Assert.True(result.HasHeader(TraceContextPropagator.TraceParentHeaderName));
        var traceParent = result.GetHeader<string>(TraceContextPropagator.TraceParentHeaderName);
        Assert.Contains(activity.TraceId.ToString(), traceParent!);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void InjectCurrent_WithNoCurrentActivity_ReturnsOriginalEnvelope()
    {
        // Arrange
        var envelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 1, 2, 3 });

        // Act
        var result = TraceContextPropagator.InjectCurrent(envelope);

        // Assert
        Assert.Equal(envelope, result);
        Assert.False(result.HasHeader(TraceContextPropagator.TraceParentHeaderName));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Extract_WithValidTraceParent_ReturnsActivityContext()
    {
        // Arrange
        var traceId = ActivityTraceId.CreateRandom();
        var spanId = ActivitySpanId.CreateRandom();
        var traceParent = $"00-{traceId}-{spanId}-01";
        var envelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 1, 2, 3 })
            .WithHeader(TraceContextPropagator.TraceParentHeaderName, traceParent);

        // Act
        var context = TraceContextPropagator.Extract(envelope);

        // Assert
        Assert.NotEqual(default, context);
        Assert.Equal(traceId, context.TraceId);
        Assert.Equal(spanId, context.SpanId);
        Assert.True(context.IsRemote);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Extract_WithTraceState_IncludesTraceState()
    {
        // Arrange
        var traceId = ActivityTraceId.CreateRandom();
        var spanId = ActivitySpanId.CreateRandom();
        var traceParent = $"00-{traceId}-{spanId}-01";
        var traceState = "vendor1=value1,vendor2=value2";
        var envelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 1, 2, 3 })
            .WithHeader(TraceContextPropagator.TraceParentHeaderName, traceParent)
            .WithHeader(TraceContextPropagator.TraceStateHeaderName, traceState);

        // Act
        var context = TraceContextPropagator.Extract(envelope);

        // Assert
        Assert.NotEqual(default, context);
        Assert.Equal(traceState, context.TraceState);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Extract_WithNoTraceParent_ReturnsDefaultContext()
    {
        // Arrange
        var envelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 1, 2, 3 });

        // Act
        var context = TraceContextPropagator.Extract(envelope);

        // Assert
        Assert.Equal(default, context);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Extract_WithInvalidTraceParent_ReturnsDefaultContext()
    {
        // Arrange
        var envelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 1, 2, 3 })
            .WithHeader(TraceContextPropagator.TraceParentHeaderName, "invalid-trace-parent");

        // Act
        var context = TraceContextPropagator.Extract(envelope);

        // Assert
        Assert.Equal(default, context);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Extract_WithMalformedTraceParent_ReturnsDefaultContext()
    {
        // Arrange
        var envelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 1, 2, 3 })
            .WithHeader(TraceContextPropagator.TraceParentHeaderName, "00-abc-def-01");

        // Act
        var context = TraceContextPropagator.Extract(envelope);

        // Assert
        Assert.Equal(default, context);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RoundTrip_InjectAndExtract_PreservesContext()
    {
        // Arrange
        using var originalActivity = new Activity("test");
        originalActivity.SetIdFormat(ActivityIdFormat.W3C);
        originalActivity.TraceStateString = "vendor=value";
        originalActivity.Start();

        var envelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 1, 2, 3 });

        // Act
        var injected = TraceContextPropagator.Inject(envelope, originalActivity);
        var extracted = TraceContextPropagator.Extract(injected);

        // Assert
        Assert.Equal(originalActivity.TraceId, extracted.TraceId);
        Assert.Equal(originalActivity.SpanId, extracted.SpanId);
        Assert.Equal(originalActivity.TraceStateString, extracted.TraceState);
        Assert.True(extracted.IsRemote);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TryExtract_WithValidTraceParent_ReturnsTrue()
    {
        // Arrange
        var traceId = ActivityTraceId.CreateRandom();
        var spanId = ActivitySpanId.CreateRandom();
        var traceParent = $"00-{traceId}-{spanId}-01";
        var envelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 1, 2, 3 })
            .WithHeader(TraceContextPropagator.TraceParentHeaderName, traceParent);

        // Act
        var result = TraceContextPropagator.TryExtract(envelope, out var context);

        // Assert
        Assert.True(result);
        Assert.NotEqual(default, context);
        Assert.Equal(traceId, context.TraceId);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TryExtract_WithNoTraceParent_ReturnsFalse()
    {
        // Arrange
        var envelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 1, 2, 3 });

        // Act
        var result = TraceContextPropagator.TryExtract(envelope, out var context);

        // Assert
        Assert.False(result);
        Assert.Equal(default, context);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TryExtract_WithInvalidTraceParent_ReturnsFalse()
    {
        // Arrange
        var envelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 1, 2, 3 })
            .WithHeader(TraceContextPropagator.TraceParentHeaderName, "invalid");

        // Act
        var result = TraceContextPropagator.TryExtract(envelope, out var context);

        // Assert
        Assert.False(result);
        Assert.Equal(default, context);
    }
}
