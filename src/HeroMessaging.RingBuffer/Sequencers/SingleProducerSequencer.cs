using HeroMessaging.RingBuffer.Sequences;
using HeroMessaging.RingBuffer.WaitStrategies;

namespace HeroMessaging.RingBuffer.Sequencers;

/// <summary>
/// Optimized sequencer for single producer scenarios.
/// Does NOT use Compare-And-Swap (CAS) operations, making it faster than multi-producer.
/// Use when only one thread publishes to the ring buffer.
/// </summary>
public sealed class SingleProducerSequencer : Sequencer
{
    private readonly PaddedLong _cursor = new(-1);
    private readonly PaddedLong _nextValue = new(-1);
    private readonly PaddedLong _cachedGatingSequence = new(-1);

    /// <summary>
    /// Creates a new single-producer sequencer
    /// </summary>
    /// <param name="bufferSize">Size of the ring buffer (must be power of 2)</param>
    /// <param name="waitStrategy">Strategy for waiting when buffer is full</param>
    public SingleProducerSequencer(int bufferSize, IWaitStrategy waitStrategy)
        : base(bufferSize, waitStrategy)
    {
    }

    /// <summary>
    /// Claim the next sequence number.
    /// No CAS operation needed since we're single-threaded.
    /// </summary>
    public override long Next()
    {
        long nextSequence = _nextValue.Value + 1;
        long wrapPoint = nextSequence - _bufferSize;
        long cachedGatingSequence = _cachedGatingSequence.Value;

        // Check if we're about to lap the slowest consumer
        if (wrapPoint > cachedGatingSequence)
        {
            // Refresh the cached gating sequence
            long minSequence = GetMinimumGatingSequence(nextSequence);
            _cachedGatingSequence.Value = minSequence;

            // Still not enough space? Wait for consumers to catch up
            while (wrapPoint > minSequence)
            {
                _waitStrategy.WaitFor(minSequence);
                minSequence = GetMinimumGatingSequence(nextSequence);
                _cachedGatingSequence.Value = minSequence;
            }
        }

        _nextValue.Value = nextSequence;
        return nextSequence;
    }

    /// <summary>
    /// Claim a batch of sequence numbers
    /// </summary>
    public override long Next(int n)
    {
        if (n < 1 || n > _bufferSize)
        {
            throw new ArgumentException(
                $"Batch size must be between 1 and {_bufferSize}", nameof(n));
        }

        long nextSequence = _nextValue.Value + n;
        long wrapPoint = nextSequence - _bufferSize;
        long cachedGatingSequence = _cachedGatingSequence.Value;

        if (wrapPoint > cachedGatingSequence)
        {
            long minSequence = GetMinimumGatingSequence(nextSequence);
            _cachedGatingSequence.Value = minSequence;

            while (wrapPoint > minSequence)
            {
                _waitStrategy.WaitFor(minSequence);
                minSequence = GetMinimumGatingSequence(nextSequence);
                _cachedGatingSequence.Value = minSequence;
            }
        }

        _nextValue.Value = nextSequence;
        return nextSequence;
    }

    /// <summary>
    /// Publish a single sequence.
    /// Simple write, no CAS needed.
    /// </summary>
    public override void Publish(long sequence)
    {
        _cursor.Value = sequence;
        _waitStrategy.SignalAllWhenBlocking();
    }

    /// <summary>
    /// Publish a range of sequences
    /// </summary>
    public override void Publish(long lo, long hi)
    {
        // For single producer, just publish the highest sequence
        Publish(hi);
    }

    /// <summary>
    /// Get the current cursor (highest published sequence)
    /// </summary>
    public long GetCursor()
    {
        return _cursor.Value;
    }
}
