using System.Globalization;
using System.Text;

namespace HeroMessaging.Transport.RabbitMQ;

/// <summary>
/// Reads the source queue of a dead-lettered RabbitMQ message and prepares its headers for replay.
/// </summary>
internal static class DeadLetterReplayHeaders
{
    /// <summary>Number of times the message has been replayed from a dead-letter queue.</summary>
    internal const string ReplayCountHeader = "hero-replay-count";

    private const string DeathHeader = "x-death";
    private const string FirstDeathQueueHeader = "x-first-death-queue";

    /// <summary>
    /// Returns the queue the message failed on: <c>hero-fault-queue</c> for an error-queue copy, otherwise the queue
    /// that most recently dead-lettered it (the first <c>x-death</c> entry), falling back to
    /// <c>x-first-death-queue</c>; <see langword="null"/> when none names a queue.
    /// </summary>
    internal static string? ResolveSourceQueue(IDictionary<string, object?>? headers)
    {
        if (headers is null)
            return null;

        // An error-queue copy names its source directly; it is the most recent failure.
        if (headers.TryGetValue(FaultHeaders.Queue, out var faulted) && AsText(faulted) is { Length: > 0 } faultQueue)
            return faultQueue;

        if (headers.TryGetValue(DeathHeader, out var deaths) &&
            deaths is IEnumerable<object?> entries &&
            entries.FirstOrDefault() is IDictionary<string, object?> latest &&
            latest.TryGetValue("queue", out var queue) &&
            AsText(queue) is { Length: > 0 } fromHistory)
        {
            return fromHistory;
        }

        return headers.TryGetValue(FirstDeathQueueHeader, out var first) && AsText(first) is { Length: > 0 } firstQueue
            ? firstQueue
            : null;
    }

    /// <summary>
    /// Copies the headers for republishing: drops the broker's <c>x-delivery-count</c> (a quorum queue would
    /// otherwise count the old failures against the replayed message's new retry budget) and the
    /// <c>hero-fault-*</c> context of the replayed failure, keeps the <c>x-death</c> history and increments
    /// <see cref="ReplayCountHeader"/>.
    /// </summary>
    internal static Dictionary<string, object?> PrepareHeaders(IDictionary<string, object?>? headers)
    {
        var prepared = WithoutDeliveryCount(FaultHeaders.WithoutFaultHeaders(headers));
        var previous = prepared.TryGetValue(ReplayCountHeader, out var value) ? AsCount(value) : 0;
        prepared[ReplayCountHeader] = previous + 1;
        return prepared;
    }

    /// <summary>Copies the headers without the broker's <c>x-delivery-count</c>.</summary>
    internal static Dictionary<string, object?> WithoutDeliveryCount(IDictionary<string, object?>? headers)
    {
        var copy = headers is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : new Dictionary<string, object?>(headers, StringComparer.Ordinal);
        copy.Remove(ConsumerRetryAttempts.BrokerDeliveryCountHeader);
        return copy;
    }

    private static string? AsText(object? value) => value switch
    {
        byte[] bytes => Encoding.UTF8.GetString(bytes),
        string text => text,
        _ => null
    };

    private static long AsCount(object? value) => value switch
    {
        long number => Math.Max(number, 0),
        int number => Math.Max(number, 0),
        _ when AsText(value) is { } text && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => Math.Max(parsed, 0),
        _ => 0
    };
}
