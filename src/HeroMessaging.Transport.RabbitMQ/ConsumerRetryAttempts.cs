namespace HeroMessaging.Transport.RabbitMQ;

/// <summary>
/// Retry counts for messages this consumer has requeued after a handler failure.
/// </summary>
/// <remarks>
/// RabbitMQ classic queues do not count redeliveries, so a requeued message carries no attempt number.
/// The count is kept per consumer by message ID, bounded by <paramref name="capacity"/> with oldest-first eviction.
/// A broker-supplied <c>x-delivery-count</c> header (quorum queues) takes precedence and survives consumer restarts.
/// </remarks>
internal sealed class ConsumerRetryAttempts(int capacity = ConsumerRetryAttempts.DefaultCapacity)
{
    internal const int DefaultCapacity = 10_000;
    internal const string BrokerDeliveryCountHeader = "x-delivery-count";

#if NET9_0_OR_GREATER
    private readonly Lock _lock = new();
#else
    private readonly object _lock = new();
#endif
    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);
    private readonly Queue<string> _order = new();

    /// <summary>Returns the number of earlier failed deliveries known for the message.</summary>
    internal int GetPreviousRetries(string messageId, IDictionary<string, object?>? headers)
    {
        var local = 0;
        if (messageId.Length > 0)
        {
            lock (_lock)
                _counts.TryGetValue(messageId, out local);
        }

        return Math.Max(local, ReadBrokerDeliveryCount(headers));
    }

    internal void Record(string messageId, int retries)
    {
        if (messageId.Length == 0)
            return;

        lock (_lock)
        {
            if (!_counts.ContainsKey(messageId))
            {
                while (_order.Count >= capacity && _order.TryDequeue(out var oldest))
                    _counts.Remove(oldest);
                _order.Enqueue(messageId);
            }

            _counts[messageId] = retries;
        }
    }

    internal void Clear(string messageId)
    {
        if (messageId.Length == 0)
            return;

        lock (_lock)
            _counts.Remove(messageId);
    }

    private static int ReadBrokerDeliveryCount(IDictionary<string, object?>? headers)
    {
        if (headers is null || !headers.TryGetValue(BrokerDeliveryCountHeader, out var value))
            return 0;

        var count = value switch
        {
            long number => number,
            int number => number,
            short number => number,
            byte number => number,
            byte[] text when long.TryParse(System.Text.Encoding.UTF8.GetString(text), out var parsed) => parsed,
            string text when long.TryParse(text, out var parsed) => parsed,
            _ => 0L
        };

        return (int)Math.Clamp(count, 0L, int.MaxValue);
    }
}
