using HeroMessaging.RingBuffer.Sequences;
using HeroMessaging.RingBuffer.WaitStrategies;

namespace HeroMessaging.RingBuffer.Sequencers;

/// <summary>
/// Base sequencer for coordinating producers and consumers in the ring buffer.
/// Handles sequence number allocation and backpressure via gating sequences.
/// </summary>
public abstract class Sequencer
{
    protected readonly int _bufferSize;
    protected readonly IWaitStrategy _waitStrategy;
    protected readonly List<ISequence> _gatingSequences = new();

    /// <summary>
    /// Creates a new sequencer
    /// </summary>
    /// <param name="bufferSize">Size of the ring buffer (must be power of 2)</param>
    /// <param name="waitStrategy">Strategy for waiting when buffer is full</param>
    protected Sequencer(int bufferSize, IWaitStrategy waitStrategy)
    {
        _bufferSize = bufferSize;
        _waitStrategy = waitStrategy;
    }

    /// <summary>
    /// Claim the next sequence number for publishing.
    /// Blocks if buffer is full (backpressure).
    /// </summary>
    /// <returns>The claimed sequence number</returns>
    public abstract long Next();

    /// <summary>
    /// Claim a batch of sequence numbers for publishing.
    /// </summary>
    /// <param name="n">Number of sequences to claim</param>
    /// <returns>The highest claimed sequence number</returns>
    public abstract long Next(int n);

    /// <summary>
    /// Publish a single sequence number to make it available to consumers.
    /// </summary>
    /// <param name="sequence">The sequence to publish</param>
    public abstract void Publish(long sequence);

    /// <summary>
    /// Publish a range of sequence numbers to make them available to consumers.
    /// </summary>
    /// <param name="lo">The lowest sequence in the range (inclusive)</param>
    /// <param name="hi">The highest sequence in the range (inclusive)</param>
    public abstract void Publish(long lo, long hi);

    /// <summary>
    /// Add a gating sequence that this sequencer must wait for.
    /// Gating sequences represent consumers - we can't overwrite data
    /// that consumers haven't processed yet.
    /// </summary>
    /// <param name="sequence">The gating sequence to add</param>
    public void AddGatingSequence(ISequence sequence)
    {
        _gatingSequences.Add(sequence);
    }

    /// <summary>
    /// Remove a gating sequence (when a consumer is removed)
    /// </summary>
    /// <param name="sequence">The gating sequence to remove</param>
    public bool RemoveGatingSequence(ISequence sequence)
    {
        return _gatingSequences.Remove(sequence);
    }

    /// <summary>
    /// Get the minimum sequence from all gating sequences.
    /// This represents the slowest consumer - we can't publish past this point.
    /// </summary>
    /// <param name="defaultValue">Value to return if no gating sequences exist</param>
    /// <returns>The minimum sequence value</returns>
    protected long GetMinimumGatingSequence(long defaultValue = long.MaxValue)
    {
        if (_gatingSequences.Count == 0)
            return defaultValue;

        long min = long.MaxValue;
        foreach (var sequence in _gatingSequences)
        {
            long value = sequence.Value;
            if (value < min)
                min = value;
        }
        return min;
    }
}
