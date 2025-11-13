using HeroMessaging.RingBuffer.Sequences;
using HeroMessaging.RingBuffer.WaitStrategies;

namespace HeroMessaging.RingBuffer.Sequencers;

/// <summary>
/// Multi-producer sequencer using Compare-And-Swap (CAS) operations for coordination.
/// Thread-safe for multiple concurrent publishers.
/// Slightly slower than single-producer due to CAS overhead, but still lock-free.
/// </summary>
public sealed class MultiProducerSequencer : Sequencer
{
    private readonly PaddedLong _cursor = new(-1);
    private readonly int[] _availableBuffer;
    private readonly int _indexMask;
    private readonly int _indexShift;

    /// <summary>
    /// Creates a new multi-producer sequencer
    /// </summary>
    /// <param name="bufferSize">Size of the ring buffer (must be power of 2)</param>
    /// <param name="waitStrategy">Strategy for waiting when buffer is full</param>
    public MultiProducerSequencer(int bufferSize, IWaitStrategy waitStrategy)
        : base(bufferSize, waitStrategy)
    {
        _availableBuffer = new int[bufferSize];
        _indexMask = bufferSize - 1;
        _indexShift = Log2(bufferSize);
        InitializeAvailableBuffer();
    }

    /// <summary>
    /// Claim the next sequence using CAS
    /// </summary>
    public override long Next()
    {
        long current;
        long next;

        do
        {
            current = _cursor.Value;
            next = current + 1;
            long wrapPoint = next - _bufferSize;
            long cachedGatingSequence = GetMinimumGatingSequence(next);

            // Check if buffer is full
            if (wrapPoint > cachedGatingSequence)
            {
                // Wait for consumers to catch up
                _waitStrategy.WaitFor(cachedGatingSequence);
                continue;
            }
        }
        // Use CAS to claim the sequence atomically
        while (Interlocked.CompareExchange(ref _cursor.Value, next, current) != current);

        return next;
    }

    /// <summary>
    /// Claim a batch of sequences using CAS
    /// </summary>
    public override long Next(int n)
    {
        if (n < 1 || n > _bufferSize)
        {
            throw new ArgumentException(
                $"Batch size must be between 1 and {_bufferSize}", nameof(n));
        }

        long current;
        long next;

        do
        {
            current = _cursor.Value;
            next = current + n;
            long wrapPoint = next - _bufferSize;
            long cachedGatingSequence = GetMinimumGatingSequence(next);

            if (wrapPoint > cachedGatingSequence)
            {
                _waitStrategy.WaitFor(cachedGatingSequence);
                continue;
            }
        }
        while (Interlocked.CompareExchange(ref _cursor.Value, next, current) != current);

        return next;
    }

    /// <summary>
    /// Publish a sequence by marking it as available
    /// </summary>
    public override void Publish(long sequence)
    {
        SetAvailable(sequence);
        _waitStrategy.SignalAllWhenBlocking();
    }

    /// <summary>
    /// Publish a range of sequences
    /// </summary>
    public override void Publish(long lo, long hi)
    {
        for (long seq = lo; seq <= hi; seq++)
        {
            SetAvailable(seq);
        }
        _waitStrategy.SignalAllWhenBlocking();
    }

    /// <summary>
    /// Mark a sequence as available for consumption
    /// </summary>
    private void SetAvailable(long sequence)
    {
        int index = CalculateIndex(sequence);
        int flag = CalculateAvailabilityFlag(sequence);
        _availableBuffer[index] = flag;
    }

    /// <summary>
    /// Check if a sequence is available for consumption
    /// </summary>
    public bool IsAvailable(long sequence)
    {
        int index = CalculateIndex(sequence);
        int flag = CalculateAvailabilityFlag(sequence);
        return _availableBuffer[index] == flag;
    }

    /// <summary>
    /// Get the highest published sequence up to a given value
    /// </summary>
    public long GetHighestPublishedSequence(long lowerBound, long availableSequence)
    {
        for (long seq = lowerBound; seq <= availableSequence; seq++)
        {
            if (!IsAvailable(seq))
            {
                return seq - 1;
            }
        }
        return availableSequence;
    }

    private int CalculateIndex(long sequence)
    {
        return ((int)sequence) & _indexMask;
    }

    private int CalculateAvailabilityFlag(long sequence)
    {
        return (int)((ulong)sequence >> _indexShift);
    }

    private void InitializeAvailableBuffer()
    {
        for (int i = 0; i < _availableBuffer.Length; i++)
        {
            _availableBuffer[i] = -1;
        }
    }

    private static int Log2(int value)
    {
        int result = 0;
        while ((value >>= 1) != 0)
        {
            result++;
        }
        return result;
    }

    /// <summary>
    /// Get the current cursor (highest claimed sequence)
    /// </summary>
    public long GetCursor()
    {
        return _cursor.Value;
    }
}
