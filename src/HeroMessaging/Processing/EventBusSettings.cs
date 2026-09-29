using HeroMessaging.Abstractions.Configuration;

namespace HeroMessaging.Processing;

internal readonly record struct EventBusSettings(int MaxDegreeOfParallelism, int BoundedCapacity, int MaxPooledEnvelopes)
{
    internal static EventBusSettings Resolve(EventBusOptions options, int processorCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processorCount);

        var parallelism = options.MaxDegreeOfParallelism ?? Math.Min(processorCount, 32);
        var capacity = options.BoundedCapacity ?? (int)Math.Min((long)processorCount * 128, ProcessingConstants.EventBusBoundedCapacity);
        var poolSize = options.MaxPooledEnvelopes ?? (int)Math.Min((long)processorCount * 8, 64);

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(parallelism, nameof(options.MaxDegreeOfParallelism));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity, nameof(options.BoundedCapacity));
        ArgumentOutOfRangeException.ThrowIfNegative(poolSize, nameof(options.MaxPooledEnvelopes));

        return new EventBusSettings(parallelism, capacity, poolSize);
    }
}
