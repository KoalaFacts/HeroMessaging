using HeroMessaging.RingBuffer.EventFactories;
using HeroMessaging.RingBuffer.Sequences;
using HeroMessaging.RingBuffer.Sequencers;
using HeroMessaging.RingBuffer.WaitStrategies;

namespace HeroMessaging.RingBuffer;

/// <summary>
/// High-performance lock-free ring buffer for message passing.
/// Uses power-of-2 sizing and sequence numbers for optimal CPU cache performance.
/// Pre-allocates all slots to eliminate allocation overhead during publishing.
/// </summary>
/// <typeparam name="T">The type of events stored in the ring buffer</typeparam>
public sealed class RingBuffer<T> where T : class
{
    private readonly T[] _entries;
    private readonly int _bufferMask;
    private readonly Sequencer _sequencer;
    private readonly IEventFactory<T> _eventFactory;
    private readonly int _bufferSize;

    /// <summary>
    /// Creates a new ring buffer
    /// </summary>
    /// <param name="bufferSize">Size of the buffer (must be power of 2)</param>
    /// <param name="eventFactory">Factory for pre-allocating events</param>
    /// <param name="producerType">Single or multi-producer mode</param>
    /// <param name="waitStrategy">Strategy for waiting when buffer is full</param>
    public RingBuffer(
        int bufferSize,
        IEventFactory<T> eventFactory,
        ProducerType producerType,
        IWaitStrategy waitStrategy)
    {
        // Validate power of 2
        if (!IsPowerOf2(bufferSize))
        {
            throw new ArgumentException(
                "Buffer size must be power of 2 for optimal performance",
                nameof(bufferSize));
        }

        if (bufferSize < 1)
        {
            throw new ArgumentException(
                "Buffer size must be positive",
                nameof(bufferSize));
        }

        _bufferSize = bufferSize;
        _entries = new T[bufferSize];
        _bufferMask = bufferSize - 1;
        _eventFactory = eventFactory ?? throw new ArgumentNullException(nameof(eventFactory));

        // Pre-allocate all entries to avoid allocation during publishing
        for (int i = 0; i < bufferSize; i++)
        {
            _entries[i] = eventFactory.Create();
        }

        // Create appropriate sequencer based on producer type
        _sequencer = producerType == ProducerType.Single
            ? new SingleProducerSequencer(bufferSize, waitStrategy)
            : new MultiProducerSequencer(bufferSize, waitStrategy);
    }

    /// <summary>
    /// Gets the buffer size
    /// </summary>
    public int BufferSize => _bufferSize;

    /// <summary>
    /// Claim the next sequence number for publishing.
    /// This is the first step in the two-phase commit pattern.
    /// </summary>
    /// <returns>The claimed sequence number</returns>
    public long Next()
    {
        return _sequencer.Next();
    }

    /// <summary>
    /// Claim a batch of sequence numbers for publishing.
    /// </summary>
    /// <param name="n">Number of sequences to claim</param>
    /// <returns>The highest claimed sequence number</returns>
    public long Next(int n)
    {
        return _sequencer.Next(n);
    }

    /// <summary>
    /// Gets the event at the specified sequence for writing.
    /// Use this between Next() and Publish() to populate the event.
    /// </summary>
    /// <param name="sequence">The sequence number</param>
    /// <returns>The pre-allocated event at this sequence</returns>
    /// <remarks>
    /// This method is in the hot path and does not validate the sequence for performance.
    /// Callers must ensure the sequence was obtained from Next() or is within valid bounds.
    /// </remarks>
    public T Get(long sequence)
    {
        System.Diagnostics.Debug.Assert(sequence >= 0, "Sequence must be non-negative");

        // Fast modulo using bitwise AND (only works with power of 2)
        return _entries[sequence & _bufferMask];
    }

    /// <summary>
    /// Publish the event at the specified sequence to make it available to consumers.
    /// This is the second step in the two-phase commit pattern.
    /// </summary>
    /// <param name="sequence">The sequence to publish</param>
    public void Publish(long sequence)
    {
        _sequencer.Publish(sequence);
    }

    /// <summary>
    /// Publish a range of sequences.
    /// Used when publishing a batch of events.
    /// </summary>
    /// <param name="lo">The lowest sequence in the range (inclusive)</param>
    /// <param name="hi">The highest sequence in the range (inclusive)</param>
    public void Publish(long lo, long hi)
    {
        _sequencer.Publish(lo, hi);
    }

    /// <summary>
    /// Create a new sequence barrier for coordinating event processors.
    /// Event processors wait on the barrier to know when new events are available.
    /// </summary>
    /// <param name="sequencesToTrack">Optional sequences to track as dependencies</param>
    /// <returns>A new sequence barrier</returns>
    public ISequenceBarrier NewBarrier(params ISequence[] sequencesToTrack)
    {
        return new SequenceBarrier(_sequencer, sequencesToTrack);
    }

    /// <summary>
    /// Add a gating sequence (typically from an event processor).
    /// Gating sequences prevent the ring buffer from overwriting data
    /// that hasn't been consumed yet.
    /// </summary>
    /// <param name="sequence">The gating sequence to add</param>
    public void AddGatingSequence(ISequence sequence)
    {
        _sequencer.AddGatingSequence(sequence);
    }

    /// <summary>
    /// Remove a gating sequence (when an event processor is removed)
    /// </summary>
    /// <param name="sequence">The gating sequence to remove</param>
    public bool RemoveGatingSequence(ISequence sequence)
    {
        return _sequencer.RemoveGatingSequence(sequence);
    }

    /// <summary>
    /// Get the minimum gating sequence (slowest consumer)
    /// </summary>
    public long GetMinimumGatingSequence()
    {
        return _sequencer.GetMinimumGatingSequence(-1);
    }

    /// <summary>
    /// Get the cursor (highest published sequence)
    /// </summary>
    public long GetCursor()
    {
        return _sequencer switch
        {
            SingleProducerSequencer single => single.GetCursor(),
            MultiProducerSequencer multi => multi.GetCursor(),
            _ => throw new InvalidOperationException($"Unknown sequencer type: {_sequencer.GetType().Name}")
        };
    }

    /// <summary>
    /// Calculate the remaining capacity in the ring buffer
    /// </summary>
    public long GetRemainingCapacity()
    {
        long consumed = GetMinimumGatingSequence();
        long produced = GetCursor();
        return _bufferSize - (produced - consumed);
    }

    private static bool IsPowerOf2(int value)
    {
        return value > 0 && (value & (value - 1)) == 0;
    }

    /// <summary>
    /// Sequence barrier for coordinating event processors
    /// </summary>
    private class SequenceBarrier : ISequenceBarrier
    {
        private readonly Sequencer _sequencer;
        private readonly ISequence[] _dependentSequences;
        private volatile bool _alerted;

        public SequenceBarrier(Sequencer sequencer, params ISequence[] dependentSequences)
        {
            _sequencer = sequencer;
            _dependentSequences = dependentSequences;
        }

        private const int SpinIterations = 100;
        private const int YieldIterations = 100;

        public long WaitFor(long sequence)
        {
            CheckAlert();

            // Wait for the sequencer to publish up to this sequence
            long availableSequence = GetCursorSequence();

            // Also wait for all dependent sequences
            if (_dependentSequences.Length > 0)
            {
                long minSequence = GetMinimumSequence();
                if (minSequence < availableSequence)
                {
                    availableSequence = minSequence;
                }
            }

            // Progressive backoff: spin -> yield -> sleep
            int spinCount = 0;
            while (availableSequence < sequence)
            {
                CheckAlert();

                // Progressive backoff to reduce CPU usage
                if (spinCount < SpinIterations)
                {
                    Thread.SpinWait(1);
                }
                else if (spinCount < SpinIterations + YieldIterations)
                {
                    Thread.Yield();
                }
                else
                {
                    Thread.Sleep(0);
                }
                spinCount++;

                availableSequence = GetCursorSequence();

                if (_dependentSequences.Length > 0)
                {
                    long minSequence = GetMinimumSequence();
                    if (minSequence < availableSequence)
                    {
                        availableSequence = minSequence;
                    }
                }
            }

            return availableSequence;
        }

        public void Alert()
        {
            _alerted = true;
        }

        public void ClearAlert()
        {
            _alerted = false;
        }

        private void CheckAlert()
        {
            if (_alerted)
            {
                throw new AlertException();
            }
        }

        private long GetCursorSequence()
        {
            return _sequencer switch
            {
                SingleProducerSequencer single => single.GetCursor(),
                MultiProducerSequencer multi => multi.GetCursor(),
                _ => throw new InvalidOperationException($"Unknown sequencer type: {_sequencer.GetType().Name}")
            };
        }

        private long GetMinimumSequence()
        {
            long min = long.MaxValue;
            foreach (var seq in _dependentSequences)
            {
                long value = seq.Value;
                if (value < min)
                {
                    min = value;
                }
            }
            return min;
        }
    }
}

/// <summary>
/// Sequence barrier interface for coordinating event processors
/// </summary>
public interface ISequenceBarrier
{
    /// <summary>
    /// Wait for the given sequence to become available
    /// </summary>
    /// <param name="sequence">The sequence to wait for</param>
    /// <returns>The available sequence (may be higher than requested)</returns>
    long WaitFor(long sequence);

    /// <summary>
    /// Alert waiting processors (for shutdown)
    /// </summary>
    void Alert();

    /// <summary>
    /// Clear the alert status
    /// </summary>
    void ClearAlert();
}

/// <summary>
/// Exception thrown when a sequence barrier is alerted
/// </summary>
public class AlertException : Exception
{
    public AlertException() : base("Sequence barrier alerted") { }
}
