using HeroMessaging.Abstractions.Transport;
using System.Diagnostics;

namespace HeroMessaging.Observability.OpenTelemetry;

/// <summary>
/// W3C Trace Context propagator for injecting and extracting trace context from transport envelopes
/// Implements the W3C Trace Context specification for distributed tracing
/// </summary>
/// <remarks>
/// This class implements the W3C Trace Context specification (https://www.w3.org/TR/trace-context/)
/// Format: traceparent: 00-{trace-id}-{parent-id}-{trace-flags}
/// </remarks>
public static class TraceContextPropagator
{
    /// <summary>
    /// W3C traceparent header name
    /// </summary>
    public const string TraceParentHeaderName = "traceparent";

    /// <summary>
    /// W3C tracestate header name
    /// </summary>
    public const string TraceStateHeaderName = "tracestate";

    private const string Version = "00";
    private const int TraceIdLength = 32;
    private const int SpanIdLength = 16;

    /// <summary>
    /// Inject trace context from an activity into a transport envelope
    /// </summary>
    /// <param name="envelope">Transport envelope to inject context into</param>
    /// <param name="activity">Activity containing the trace context</param>
    /// <returns>Updated envelope with trace context headers</returns>
    public static TransportEnvelope Inject(TransportEnvelope envelope, Activity? activity)
    {
        if (activity == null)
        {
            return envelope;
        }

        // Format: 00-{trace-id}-{span-id}-{trace-flags}
        var traceParent = $"{Version}-{activity.TraceId}-{activity.SpanId}-{(activity.Recorded ? "01" : "00")}";
        var result = envelope.WithHeader(TraceParentHeaderName, traceParent);

        // Add tracestate if present
        if (!string.IsNullOrEmpty(activity.TraceStateString))
        {
            result = result.WithHeader(TraceStateHeaderName, activity.TraceStateString);
        }

        return result;
    }

    /// <summary>
    /// Inject trace context from the current activity into a transport envelope
    /// </summary>
    /// <param name="envelope">Transport envelope to inject context into</param>
    /// <returns>Updated envelope with trace context headers</returns>
    public static TransportEnvelope InjectCurrent(TransportEnvelope envelope)
    {
        return Inject(envelope, Activity.Current);
    }

    /// <summary>
    /// Extract trace context from a transport envelope
    /// </summary>
    /// <param name="envelope">Transport envelope containing trace context headers</param>
    /// <returns>ActivityContext with extracted trace information, or default if no valid context found</returns>
    public static ActivityContext Extract(TransportEnvelope envelope)
    {
        if (!envelope.HasHeader(TraceParentHeaderName))
        {
            return default;
        }

        var traceParent = envelope.GetHeader<string>(TraceParentHeaderName);
        if (string.IsNullOrEmpty(traceParent))
        {
            return default;
        }

        if (!TryParseTraceParent(traceParent, out var traceId, out var spanId, out var traceFlags))
        {
            return default;
        }

        var traceState = envelope.GetHeader<string>(TraceStateHeaderName);

        return new ActivityContext(
            traceId,
            spanId,
            traceFlags,
            traceState,
            isRemote: true);
    }

    /// <summary>
    /// Try to extract trace context from a transport envelope
    /// </summary>
    /// <param name="envelope">Transport envelope containing trace context headers</param>
    /// <param name="context">Extracted activity context</param>
    /// <returns>True if context was successfully extracted, false otherwise</returns>
    public static bool TryExtract(TransportEnvelope envelope, out ActivityContext context)
    {
        context = Extract(envelope);
        return context != default;
    }

    private static bool TryParseTraceParent(
        string traceParent,
        out ActivityTraceId traceId,
        out ActivitySpanId spanId,
        out ActivityTraceFlags traceFlags)
    {
        traceId = default;
        spanId = default;
        traceFlags = ActivityTraceFlags.None;

        // Format: 00-{trace-id}-{span-id}-{trace-flags}
        var parts = traceParent.Split('-');
        if (parts.Length != 4)
        {
            return false;
        }

        // Validate version
        if (parts[0] != Version)
        {
            return false;
        }

        // Parse trace ID
        if (parts[1].Length != TraceIdLength)
        {
            return false;
        }

        if (!ActivityTraceId.TryCreateFromString(parts[1].AsSpan(), out traceId))
        {
            return false;
        }

        // Parse span ID
        if (parts[2].Length != SpanIdLength)
        {
            return false;
        }

        if (!ActivitySpanId.TryCreateFromString(parts[2].AsSpan(), out spanId))
        {
            return false;
        }

        // Parse trace flags
        if (parts[3].Length != 2)
        {
            return false;
        }

        if (!byte.TryParse(parts[3], System.Globalization.NumberStyles.HexNumber, null, out var flagsByte))
        {
            return false;
        }

        traceFlags = (ActivityTraceFlags)flagsByte;

        return true;
    }
}
