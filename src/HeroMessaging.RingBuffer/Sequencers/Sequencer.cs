using HeroMessaging.RingBuffer.Sequences;
using HeroMessaging.RingBuffer.WaitStrategies;

namespace HeroMessaging.RingBuffer.Sequencers;

/// <summary>
/// Base sequencer for coordinating producers and consumers in the ring buffer.
/// Handles sequence number allocation and backpressure via gating sequences.
/// Thread-safe for adding/removing gating sequences.
/// </summary>
public abstract class Sequencer
{
    protected readonly int _bufferSize;
    protected readonly IWaitStrategy _waitStrategy;

    // Use volatile array reference for lock-free reads in hot path
    private volatile ISequence[] _gatingSequencesArray = Array.Empty<ISequence>();
    private readonly object _gatingLock = new();

    /// <summary>
    /// Creates a new sequencer
    /// </summary>
    /// <param name="bufferSize">Size of the ring buffer (must be power of 2)</param>
    /// <param name="waitStrategy">Strategy for waiting when buffer is full</param>
    protected Sequencer(int bufferSize, IWaitStrategy waitStrategy)
    {
        _bufferSize = bufferSize;
        _waitStrategy = waitStrategy ?? throw new ArgumentNullException(nameof(waitStrategy));
    }

    /// <summary>
    /// Gets the current cursor position (highest published sequence)
    /// </summary>
    public abstract long GetCursor();

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
    /// Gating sequences represent consumers - we cannot overwrite data
    /// that consumers have not processed yet.
    /// </summary>
    /// <param name="sequence">The gating sequence to add</param>
    public void AddGatingSequence(ISequence sequence)
    {
        if (sequence == null)
            throw new ArgumentNullException(nameof(sequence));

        lock (_gatingLock)
        {
            var current = _gatingSequencesArray;
            var newArray = new ISequence[current.Length + 1];
            Array.Copy(current, newArray, current.Length);
            newArray[current.Length] = sequence;
            Volatile.Write(ref _gatingSequencesArray, newArray);
        }
    }

    /// <summary>
    /// Remove a gating sequence (when a consumer is removed)
    /// </summary>
    /// <param name="sequence">The gating sequence to remove</param>
    public bool RemoveGatingSequence(ISequence sequence)
    {
        lock (_gatingLock)
        {
            var current = _gatingSequencesArray;
            int index = Array.IndexOf(current, sequence);
            if (index < 0)
                return false;

            var newArray = new ISequence[current.Length - 1];
            if (index > 0)
                Array.Copy(current, 0, newArray, 0, index);
            if (index < current.Length - 1)
                Array.Copy(current, index + 1, newArray, index, current.Length - index - 1);

            Volatile.Write(ref _gatingSequencesArray, newArray);
            return true;
        }
    }

    /// <summary>
    /// Get the minimum sequence from all gating sequences.
    /// This represents the slowest consumer - we cannot publish past this point.
    /// Lock-free for high performance in the hot path.
    /// </summary>
    /// <param name="defaultValue">Value to return if no gating sequences exist</param>
    /// <returns>The minimum sequence value</returns>
    public long GetMinimumGatingSequence(long defaultValue = long.MaxValue)
    {
        // Lock-free read of the array reference
        var sequences = Volatile.Read(ref _gatingSequencesArray);
        if (sequences.Length == 0)
            return defaultValue;

        long min = long.MaxValue;
        foreach (var sequence in sequences)
        {
            long value = sequence.Value;
            if (value < min)
                min = value;
        }
        return min;
    }
}
