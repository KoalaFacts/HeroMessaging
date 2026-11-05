using System.Globalization;

namespace HeroMessaging.Transport.RabbitMQ;

/// <summary>
/// Retry counts for messages this consumer has requeued after a handler failure.
/// </summary>
/// <remarks>
/// RabbitMQ classic queues do not count redeliveries, so a requeued message carries no attempt number.
/// The count is kept per consumer by message ID (copies with the same ID share it), bounded by
/// <paramref name="capacity"/> with least-recently-recorded eviction. A broker-supplied <c>x-delivery-count</c>
/// header (quorum queues) is used as a floor and survives consumer restarts.
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
    private readonly Dictionary<string, LinkedListNode<(string MessageId, int Retries)>> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<(string MessageId, int Retries)> _order = new();

    /// <summary>Number of tracked message IDs.</summary>
    internal int Count
    {
        get
        {
            lock (_lock)
                return _entries.Count;
        }
    }

    /// <summary>
    /// Returns the number of earlier failed deliveries known for the message, or <see langword="null"/> when
    /// neither a message ID nor a broker count is available.
    /// </summary>
    internal int? GetPreviousRetries(string messageId, IDictionary<string, object?>? headers)
    {
        var broker = ReadBrokerDeliveryCount(headers);
        if (messageId.Length == 0)
            return broker;

        lock (_lock)
        {
            var local = _entries.TryGetValue(messageId, out var node) ? node.Value.Retries : 0;
            return Math.Max(local, broker ?? 0);
        }
    }

    /// <summary>
    /// Atomically records one more retry for the message and returns the new retry number.
    /// </summary>
    internal int Increment(string messageId, int previousRetries)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(messageId, out var node))
            {
                var next = Math.Max(node.Value.Retries, previousRetries) + 1;
                _order.Remove(node);
                node.Value = (messageId, next);
                _order.AddLast(node);
                return next;
            }

            while (_entries.Count >= capacity && _order.First is { } oldest)
            {
                _order.RemoveFirst();
                _entries.Remove(oldest.Value.MessageId);
            }

            var retries = previousRetries + 1;
            _entries[messageId] = _order.AddLast((messageId, retries));
            return retries;
        }
    }

    internal void Clear(string messageId)
    {
        if (messageId.Length == 0)
            return;

        lock (_lock)
        {
            if (_entries.Remove(messageId, out var node))
                _order.Remove(node);
        }
    }

    private static int? ReadBrokerDeliveryCount(IDictionary<string, object?>? headers)
    {
        if (headers is null || !headers.TryGetValue(BrokerDeliveryCountHeader, out var value))
            return null;

        long? count = value switch
        {
            long number => number,
            int number => number,
            short number => number,
            byte number => number,
            byte[] text when long.TryParse(System.Text.Encoding.UTF8.GetString(text), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            string text when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };

        return count is { } known ? (int)Math.Clamp(known, 0L, int.MaxValue) : null;
    }
}
