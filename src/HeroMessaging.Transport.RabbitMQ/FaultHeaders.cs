using System.Globalization;

namespace HeroMessaging.Transport.RabbitMQ;

/// <summary>
/// Failure context attached to a message moved to an error queue (<see cref="HeroMessaging.Abstractions.Transport.RabbitMqTransportOptions.ErrorQueueSuffix"/>).
/// </summary>
internal static class FaultHeaders
{
    internal const string Prefix = "hero-fault-";
    internal const string Queue = "hero-fault-queue";
    internal const string ExceptionType = "hero-fault-exception-type";
    internal const string ExceptionMessage = "hero-fault-exception-message";
    internal const string Reason = "hero-fault-reason";
    internal const string Retries = "hero-fault-retries";
    internal const string Time = "hero-fault-time";
    internal const string Consumer = "hero-fault-consumer";

    /// <summary>Longest exception message kept, in characters; longer messages are truncated.</summary>
    internal const int MaxMessageLength = 4096;

    /// <summary>
    /// Copies the delivered headers, replacing any earlier fault headers with this failure's context.
    /// </summary>
    internal static Dictionary<string, object?> Create(
        IDictionary<string, object?>? headers,
        string sourceQueue,
        string consumerId,
        int retries,
        DateTimeOffset failedAt,
        Exception? exception,
        string? reason)
    {
        var fault = WithoutFaultHeaders(headers);
        fault.Remove(ConsumerRetryAttempts.BrokerDeliveryCountHeader);
        fault[Queue] = sourceQueue;
        fault[Consumer] = consumerId;
        fault[Retries] = (long)retries;
        fault[Time] = failedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        if (exception is not null)
        {
            fault[ExceptionType] = exception.GetType().FullName ?? exception.GetType().Name;
            fault[ExceptionMessage] = Truncate(exception.Message);
        }

        if (reason is not null)
            fault[Reason] = reason;

        return fault;
    }

    /// <summary>Cuts <paramref name="message"/> to <see cref="MaxMessageLength"/> without splitting a surrogate pair.</summary>
    internal static string Truncate(string message)
    {
        if (message.Length <= MaxMessageLength)
            return message;

        var length = char.IsHighSurrogate(message[MaxMessageLength - 1]) ? MaxMessageLength - 1 : MaxMessageLength;
        return message[..length];
    }

    /// <summary>Copies the headers without any <c>hero-fault-*</c> entry.</summary>
    internal static Dictionary<string, object?> WithoutFaultHeaders(IDictionary<string, object?>? headers)
    {
        var copy = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (headers is null)
            return copy;

        foreach (var (key, value) in headers)
        {
            if (!key.StartsWith(Prefix, StringComparison.Ordinal))
                copy[key] = value;
        }

        return copy;
    }
}
