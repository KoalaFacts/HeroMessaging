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
    /// Returns the queue that most recently dead-lettered the message (the first <c>x-death</c> entry), falling
    /// back to <c>x-first-death-queue</c>; <see langword="null"/> when neither names a queue.
    /// </summary>
    internal static string? ResolveSourceQueue(IDictionary<string, object?>? headers)
    {
        if (headers is null)
            return null;

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
    /// otherwise count the old failures against the replayed message's new retry budget), keeps the
    /// <c>x-death</c> failure history and increments <see cref="ReplayCountHeader"/>.
    /// </summary>
    internal static Dictionary<string, object?> PrepareHeaders(IDictionary<string, object?>? headers)
    {
        var prepared = headers is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : new Dictionary<string, object?>(headers, StringComparer.Ordinal);
        prepared.Remove(ConsumerRetryAttempts.BrokerDeliveryCountHeader);
        var previous = prepared.TryGetValue(ReplayCountHeader, out var value) ? AsCount(value) : 0;
        prepared[ReplayCountHeader] = previous + 1;
        return prepared;
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
