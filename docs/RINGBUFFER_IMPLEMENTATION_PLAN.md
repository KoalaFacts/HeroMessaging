# In-House Ring Buffer Implementation Plan for HeroMessaging

**Document Version**: 1.1
**Date**: 2025-11-13
**Status**: Planning Phase
**Estimated Timeline**: 12-16 weeks
**Estimated Effort**: 480-640 hours

---

## Executive Summary

This document outlines a comprehensive plan to build high-performance ring buffer concurrent data structures in-house for HeroMessaging. Rather than taking a dependency on Disruptor.NET or similar libraries, we will implement the core lock-free ring buffer patterns tailored specifically for HeroMessaging's architecture.

### Why In-House Implementation?

1. **Zero External Dependencies**: Maintain HeroMessaging's minimal dependency philosophy
2. **Tailored Design**: Optimize specifically for messaging patterns, not general-purpose use
3. **Learning & Control**: Deep understanding of performance characteristics
4. **Integration**: Seamless integration with existing pipeline architecture
5. **Multi-Framework Support**: Native support for netstandard2.0, net8.0, net9.0, net10.0

### Expected Performance Gains

| Metric | Current (Channel) | Target (RingBuffer) | Improvement |
|--------|-------------------|---------------------|-------------|
| **Latency (p99)** | <1ms | <0.05ms | 20x faster |
| **Throughput** | 100K msg/s | 500K+ msg/s | 5x increase |
| **Allocations** | ~200B/msg | 0B/msg | Zero GC |
| **CPU Cache Misses** | Moderate | Minimal | 10x reduction |

### Implementation Strategy

**Keep Both Implementations** (Channel + RingBuffer):
- **Channel-based** (existing): Remains the **default** - async/await, flexible backpressure
- **RingBuffer-based** (new): Opt-in via configuration - lock-free, zero-allocation, ultra-low latency

**Migration Path**:
1. RingBuffer implementation is **opt-in** via `QueueMode` configuration
2. Users can A/B test and compare performance
3. Instant rollback via feature flag if issues arise
4. After 6-12 months of proven stability, consider making RingBuffer the default

---

## Phase 1: Core Ring Buffer Foundation (4 weeks, 160 hours)

### 1.1 Ring Buffer Data Structure

**File**: `src/HeroMessaging.RingBuffer/RingBuffer.cs`

**Core Responsibilities**:
- Lock-free circular buffer with power-of-2 sizing
- Sequence-based indexing for optimal CPU cache usage
- Pre-allocated memory slots
- Multi-producer and single-producer modes

**Key Components**:

```csharp
namespace HeroMessaging.RingBuffer;

/// <summary>
/// High-performance lock-free ring buffer for message passing.
/// Uses power-of-2 sizing and sequence numbers for optimal CPU cache performance.
/// </summary>
/// <typeparam name="T">The type of events stored in the ring buffer</typeparam>
public sealed class RingBuffer<T> where T : class
{
    // Core fields
    private readonly T[] _entries;              // Pre-allocated slots (power of 2)
    private readonly int _bufferMask;           // Fast modulo via bitwise AND
    private readonly Sequencer _sequencer;      // Coordinates producers/consumers
    private readonly IEventFactory<T> _eventFactory;

    // Performance: Keep sequence on separate cache lines to avoid false sharing
    private readonly PaddedLong _cursor = new(-1);

    public RingBuffer(int bufferSize, IEventFactory<T> eventFactory, ProducerType producerType)
    {
        // Validate power of 2
        if (!IsPowerOf2(bufferSize))
            throw new ArgumentException("Buffer size must be power of 2", nameof(bufferSize));

        _entries = new T[bufferSize];
        _bufferMask = bufferSize - 1;
        _eventFactory = eventFactory;

        // Pre-allocate all entries
        for (int i = 0; i < bufferSize; i++)
        {
            _entries[i] = eventFactory.Create();
        }

        _sequencer = producerType == ProducerType.Single
            ? new SingleProducerSequencer(bufferSize)
            : new MultiProducerSequencer(bufferSize);
    }

    /// <summary>
    /// Publishes an event to the ring buffer (two-phase commit pattern)
    /// </summary>
    public long Next()
    {
        return _sequencer.Next();
    }

    /// <summary>
    /// Gets the event at the specified sequence for writing
    /// </summary>
    public T Get(long sequence)
    {
        // Fast modulo using bitwise AND (only works with power of 2)
        return _entries[sequence & _bufferMask];
    }

    /// <summary>
    /// Publishes the event at the specified sequence
    /// </summary>
    public void Publish(long sequence)
    {
        _sequencer.Publish(sequence);
        _cursor.Value = sequence;
    }

    // Batch publishing methods
    public long Next(int n) => _sequencer.Next(n);
    public void Publish(long lo, long hi) => _sequencer.Publish(lo, hi);
}

/// <summary>
/// Factory for creating pre-allocated ring buffer entries
/// </summary>
public interface IEventFactory<T>
{
    T Create();
}

public enum ProducerType
{
    Single,     // Single producer - faster, no CAS operations
    Multi       // Multiple producers - uses CAS for coordination
}
```

**Implementation Details**:

1. **Power-of-2 Sizing**: Enables fast modulo via bitwise AND
   ```csharp
   // Traditional: index = sequence % size (division - slow)
   // Optimized:  index = sequence & (size - 1) (bitwise AND - fast)
   ```

2. **False Sharing Prevention**: Use cache line padding
   ```csharp
   [StructLayout(LayoutKind.Explicit, Size = 128)]
   internal struct PaddedLong
   {
       [FieldOffset(56)]
       public long Value;

       public PaddedLong(long value) => Value = value;
   }
   ```

3. **Memory Layout Optimization**: Sequential memory access for cache efficiency

**Testing Requirements**:
- Unit tests: Power-of-2 validation, indexing correctness, boundary conditions
- Concurrency tests: Multiple producers/consumers simultaneously
- Performance tests: Measure cache miss rates, throughput benchmarks
- **Coverage Target**: 100% (constitutional requirement for public APIs)

---

### 1.2 Sequencer Implementation

**Files**:
- `src/HeroMessaging.RingBuffer/Sequencer.cs` (abstract base)
- `src/HeroMessaging.RingBuffer/SingleProducerSequencer.cs`
- `src/HeroMessaging.RingBuffer/MultiProducerSequencer.cs`

**Responsibilities**:
- Coordinate sequence number allocation
- Track consumer progress (gating sequences)
- Implement backpressure when buffer is full
- Single-producer optimization (no CAS) vs multi-producer (CAS-based)

**Key Components**:

```csharp
/// <summary>
/// Base sequencer for coordinating producers and consumers
/// </summary>
public abstract class Sequencer
{
    protected readonly int _bufferSize;
    protected readonly IWaitStrategy _waitStrategy;
    protected readonly List<ISequence> _gatingSequences = new();

    protected Sequencer(int bufferSize, IWaitStrategy waitStrategy)
    {
        _bufferSize = bufferSize;
        _waitStrategy = waitStrategy;
    }

    /// <summary>
    /// Claim the next sequence number for publishing
    /// </summary>
    public abstract long Next();

    /// <summary>
    /// Claim a batch of sequence numbers
    /// </summary>
    public abstract long Next(int n);

    /// <summary>
    /// Publish a single sequence number
    /// </summary>
    public abstract void Publish(long sequence);

    /// <summary>
    /// Publish a range of sequence numbers
    /// </summary>
    public abstract void Publish(long lo, long hi);

    /// <summary>
    /// Add a gating sequence that this sequencer must wait for
    /// </summary>
    public void AddGatingSequence(ISequence sequence)
    {
        _gatingSequences.Add(sequence);
    }

    /// <summary>
    /// Get the minimum sequence from all gating sequences
    /// </summary>
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

/// <summary>
/// Optimized sequencer for single producer scenarios (no CAS required)
/// </summary>
public sealed class SingleProducerSequencer : Sequencer
{
    private readonly PaddedLong _cursor = new(-1);
    private readonly PaddedLong _nextValue = new(-1);
    private readonly PaddedLong _cachedGatingSequence = new(-1);

    public SingleProducerSequencer(int bufferSize, IWaitStrategy waitStrategy)
        : base(bufferSize, waitStrategy) { }

    public override long Next()
    {
        long nextSequence = _nextValue.Value + 1;
        long wrapPoint = nextSequence - _bufferSize;
        long cachedGatingSequence = _cachedGatingSequence.Value;

        // Check if we need to wait for consumers
        if (wrapPoint > cachedGatingSequence)
        {
            long minSequence = GetMinimumGatingSequence(nextSequence);
            _cachedGatingSequence.Value = minSequence;

            // Still not enough space, wait
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

    public override void Publish(long sequence)
    {
        // Single producer - simple write, no CAS needed
        _cursor.Value = sequence;
        _waitStrategy.SignalAllWhenBlocking();
    }
}

/// <summary>
/// Multi-producer sequencer using CAS operations for coordination
/// </summary>
public sealed class MultiProducerSequencer : Sequencer
{
    private readonly PaddedLong _cursor = new(-1);
    private readonly int[] _availableBuffer;  // Track which sequences are published
    private readonly int _indexMask;
    private readonly int _indexShift;

    public MultiProducerSequencer(int bufferSize, IWaitStrategy waitStrategy)
        : base(bufferSize, waitStrategy)
    {
        _availableBuffer = new int[bufferSize];
        _indexMask = bufferSize - 1;
        _indexShift = Log2(bufferSize);
        InitializeAvailableBuffer();
    }

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

            if (wrapPoint > cachedGatingSequence)
            {
                // Buffer full, wait
                _waitStrategy.WaitFor(cachedGatingSequence);
                continue;
            }
        }
        while (Interlocked.CompareExchange(ref _cursor.Value, next, current) != current);

        return next;
    }

    public override void Publish(long sequence)
    {
        SetAvailable(sequence);
        _waitStrategy.SignalAllWhenBlocking();
    }

    private void SetAvailable(long sequence)
    {
        int index = CalculateIndex(sequence);
        int flag = CalculateAvailabilityFlag(sequence);
        _availableBuffer[index] = flag;
    }

    private int CalculateIndex(long sequence)
    {
        return ((int)sequence) & _indexMask;
    }

    private int CalculateAvailabilityFlag(long sequence)
    {
        return (int)((ulong)sequence >> _indexShift);
    }
}

/// <summary>
/// Interface for sequences tracked by the ring buffer
/// </summary>
public interface ISequence
{
    long Value { get; set; }
}
```

**Performance Optimizations**:
1. **Cache Line Padding**: Prevent false sharing between producer/consumer sequences
2. **Batch Processing**: `Next(int n)` for claiming multiple slots at once
3. **Cached Gating Sequence**: Avoid checking all consumers on every publish
4. **CAS Only When Necessary**: Single producer avoids all atomic operations

**Testing Requirements**:
- Single producer tests: Verify no CAS operations used
- Multi-producer tests: Verify thread-safety with 10+ concurrent producers
- Backpressure tests: Verify correct blocking when buffer full
- Benchmark tests: Compare single vs multi-producer performance

---

### 1.3 Wait Strategies

**File**: `src/HeroMessaging.RingBuffer/WaitStrategies/`

**Responsibilities**:
- Control how consumers wait for new events
- Trade-off between CPU usage and latency
- Support different deployment scenarios

**Implementations**:

```csharp
namespace HeroMessaging.RingBuffer.WaitStrategies;

/// <summary>
/// Strategy for waiting on new events in the ring buffer
/// </summary>
public interface IWaitStrategy
{
    /// <summary>
    /// Wait for the given sequence to become available
    /// </summary>
    /// <param name="sequence">The sequence to wait for</param>
    /// <returns>The available sequence number</returns>
    long WaitFor(long sequence);

    /// <summary>
    /// Signal waiting consumers that new events are available
    /// </summary>
    void SignalAllWhenBlocking();
}

/// <summary>
/// Blocks using a lock and condition variable.
/// Best for scenarios where CPU usage must be minimized.
/// Latency: ~1-5ms
/// </summary>
public sealed class BlockingWaitStrategy : IWaitStrategy
{
    private readonly object _lock = new();
    private volatile bool _signalled;

    public long WaitFor(long sequence)
    {
        lock (_lock)
        {
            while (!_signalled)
            {
                Monitor.Wait(_lock);
            }
            _signalled = false;
        }
        return sequence;
    }

    public void SignalAllWhenBlocking()
    {
        lock (_lock)
        {
            _signalled = true;
            Monitor.PulseAll(_lock);
        }
    }
}

/// <summary>
/// Spins with Thread.Yield() then sleeps.
/// Progressive back-off strategy balancing CPU and latency.
/// Latency: ~100μs-1ms
/// </summary>
public sealed class SleepingWaitStrategy : IWaitStrategy
{
    private const int SpinTries = 100;
    private const int YieldTries = 100;

    public long WaitFor(long sequence)
    {
        int counter = SpinTries + YieldTries;

        while (counter > YieldTries)
        {
            // Busy spin
            counter--;
            Thread.SpinWait(1);
        }

        while (counter > 0)
        {
            // Yield to other threads
            counter--;
            Thread.Yield();
        }

        // Sleep for 1ms
        Thread.Sleep(1);
        return sequence;
    }

    public void SignalAllWhenBlocking()
    {
        // No-op for sleeping strategy
    }
}

/// <summary>
/// Spins then yields. No sleeping.
/// Low latency but uses CPU.
/// Latency: ~50-100μs
/// </summary>
public sealed class YieldingWaitStrategy : IWaitStrategy
{
    private const int SpinTries = 100;

    public long WaitFor(long sequence)
    {
        int counter = SpinTries;

        while (counter > 0)
        {
            counter--;
            Thread.SpinWait(1);
        }

        Thread.Yield();
        return sequence;
    }

    public void SignalAllWhenBlocking()
    {
        // No-op
    }
}

/// <summary>
/// Busy spins without yielding or sleeping.
/// Ultra-low latency but 100% CPU usage.
/// Latency: <10μs
/// </summary>
public sealed class BusySpinWaitStrategy : IWaitStrategy
{
    public long WaitFor(long sequence)
    {
        // Just spin forever
        while (true)
        {
            Thread.SpinWait(1);
        }
    }

    public void SignalAllWhenBlocking()
    {
        // No-op
    }
}

/// <summary>
/// Blocks with a timeout, then throws exception.
/// Best for scenarios where deadlock detection is needed.
/// </summary>
public sealed class TimeoutBlockingWaitStrategy : IWaitStrategy
{
    private readonly TimeSpan _timeout;
    private readonly object _lock = new();
    private volatile bool _signalled;

    public TimeoutBlockingWaitStrategy(TimeSpan timeout)
    {
        _timeout = timeout;
    }

    public long WaitFor(long sequence)
    {
        lock (_lock)
        {
            if (!_signalled)
            {
                if (!Monitor.Wait(_lock, _timeout))
                {
                    throw new TimeoutException(
                        $"Timeout waiting for sequence {sequence} after {_timeout}");
                }
            }
            _signalled = false;
        }
        return sequence;
    }

    public void SignalAllWhenBlocking()
    {
        lock (_lock)
        {
            _signalled = true;
            Monitor.PulseAll(_lock);
        }
    }
}
```

**Wait Strategy Selection Guide**:

| Scenario | Recommended Strategy | Latency | CPU Usage |
|----------|---------------------|---------|-----------|
| Low-latency trading | BusySpinWaitStrategy | <10μs | 100% |
| Real-time gaming | YieldingWaitStrategy | 50-100μs | High |
| General purpose | SleepingWaitStrategy | 100μs-1ms | Low |
| Resource constrained | BlockingWaitStrategy | 1-5ms | Minimal |
| Scheduled messages | TimeoutBlockingWaitStrategy | Variable | Low |

**Testing Requirements**:
- Latency benchmarks for each strategy
- CPU usage profiling
- Multi-consumer coordination tests
- Timeout tests for TimeoutBlockingWaitStrategy

---

### 1.4 Event Processor

**File**: `src/HeroMessaging.RingBuffer/EventProcessor.cs`

**Responsibilities**:
- Consume events from ring buffer
- Track processing progress (sequence)
- Coordinate with other processors via sequence barriers

```csharp
namespace HeroMessaging.RingBuffer;

/// <summary>
/// Processes events from the ring buffer
/// </summary>
public interface IEventProcessor : IDisposable
{
    /// <summary>
    /// Get the current sequence being processed
    /// </summary>
    ISequence Sequence { get; }

    /// <summary>
    /// Start processing events
    /// </summary>
    void Start();

    /// <summary>
    /// Stop processing events
    /// </summary>
    void Stop();

    /// <summary>
    /// Check if the processor is running
    /// </summary>
    bool IsRunning { get; }
}

/// <summary>
/// Batch event processor for high-throughput scenarios
/// </summary>
public sealed class BatchEventProcessor<T> : IEventProcessor where T : class
{
    private readonly RingBuffer<T> _ringBuffer;
    private readonly ISequenceBarrier _sequenceBarrier;
    private readonly IEventHandler<T> _eventHandler;
    private readonly ISequence _sequence = new Sequence(-1);
    private readonly CancellationTokenSource _cts = new();
    private Task? _processingTask;

    public BatchEventProcessor(
        RingBuffer<T> ringBuffer,
        ISequenceBarrier sequenceBarrier,
        IEventHandler<T> eventHandler)
    {
        _ringBuffer = ringBuffer;
        _sequenceBarrier = sequenceBarrier;
        _eventHandler = eventHandler;
    }

    public ISequence Sequence => _sequence;
    public bool IsRunning { get; private set; }

    public void Start()
    {
        if (IsRunning) return;

        IsRunning = true;
        _processingTask = Task.Run(ProcessEvents);
    }

    private void ProcessEvents()
    {
        long nextSequence = _sequence.Value + 1;

        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    // Wait for events to be available
                    long availableSequence = _sequenceBarrier.WaitFor(nextSequence);

                    // Process all available events in a batch
                    while (nextSequence <= availableSequence)
                    {
                        T evt = _ringBuffer.Get(nextSequence);
                        _eventHandler.OnEvent(evt, nextSequence, nextSequence == availableSequence);
                        nextSequence++;
                    }

                    // Update our sequence to signal we're done
                    _sequence.Value = availableSequence;
                }
                catch (AlertException)
                {
                    // Processor is being stopped
                    break;
                }
                catch (Exception ex)
                {
                    _eventHandler.OnError(ex);
                    _sequence.Value = nextSequence;
                    nextSequence++;
                }
            }
        }
        finally
        {
            _eventHandler.OnShutdown();
            IsRunning = false;
        }
    }

    public void Stop()
    {
        _cts.Cancel();
        _sequenceBarrier.Alert();
    }

    public void Dispose()
    {
        Stop();
        _processingTask?.Wait();
        _cts.Dispose();
    }
}

/// <summary>
/// Handler for processing events from the ring buffer
/// </summary>
public interface IEventHandler<T>
{
    /// <summary>
    /// Called for each event
    /// </summary>
    /// <param name="data">The event data</param>
    /// <param name="sequence">The sequence number</param>
    /// <param name="endOfBatch">True if this is the last event in the current batch</param>
    void OnEvent(T data, long sequence, bool endOfBatch);

    /// <summary>
    /// Called when an exception occurs during processing
    /// </summary>
    void OnError(Exception ex);

    /// <summary>
    /// Called when the processor is shutting down
    /// </summary>
    void OnShutdown();
}

/// <summary>
/// Coordinates dependencies between event processors
/// </summary>
public interface ISequenceBarrier
{
    /// <summary>
    /// Wait for the given sequence to become available
    /// </summary>
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
```

**Testing Requirements**:
- Single processor tests: Verify all events processed
- Multi-processor tests: Verify work distribution
- Exception handling tests: Verify OnError called correctly
- Shutdown tests: Verify clean disposal

---

## Phase 2: HeroMessaging Integration (3 weeks, 120 hours)

### 2.1 Configuration with Queue Mode

**File**: `src/HeroMessaging.Abstractions/Transport/InMemoryQueueOptions.cs`

**Goal**: Allow users to choose between Channel-based or RingBuffer-based implementation

```csharp
namespace HeroMessaging.Abstractions.Transport;

/// <summary>
/// Configuration options for in-memory queues
/// </summary>
public class InMemoryQueueOptions
{
    /// <summary>
    /// Queue implementation mode. Default is Channel (existing implementation).
    /// </summary>
    public QueueMode Mode { get; set; } = QueueMode.Channel;

    /// <summary>
    /// Maximum queue buffer size (must be power of 2 for RingBuffer mode)
    /// </summary>
    public int BufferSize { get; set; } = 1024;

    /// <summary>
    /// Drop oldest messages when buffer is full (Channel mode only)
    /// </summary>
    public bool DropWhenFull { get; set; } = false;

    /// <summary>
    /// Wait strategy for consumers (RingBuffer mode only)
    /// </summary>
    public WaitStrategy WaitStrategy { get; set; } = WaitStrategy.Sleeping;

    /// <summary>
    /// Producer type (RingBuffer mode only)
    /// </summary>
    public ProducerMode ProducerMode { get; set; } = ProducerMode.Multi;
}

/// <summary>
/// Queue implementation mode
/// </summary>
public enum QueueMode
{
    /// <summary>
    /// Use System.Threading.Channels (async/await, flexible backpressure)
    /// Default mode - best for general purpose use
    /// </summary>
    Channel,

    /// <summary>
    /// Use lock-free ring buffer (pre-allocated, zero GC, ultra-low latency)
    /// Opt-in mode - best for high-performance scenarios
    /// </summary>
    RingBuffer
}

/// <summary>
/// Wait strategy for RingBuffer mode
/// </summary>
public enum WaitStrategy
{
    /// <summary>
    /// Block using Monitor.Wait (lowest CPU, ~1-5ms latency)
    /// </summary>
    Blocking,

    /// <summary>
    /// Progressive backoff: spin, yield, sleep (balanced, ~100μs-1ms latency)
    /// </summary>
    Sleeping,

    /// <summary>
    /// Spin then yield (low latency, ~50-100μs)
    /// </summary>
    Yielding,

    /// <summary>
    /// Busy spin (ultra-low latency <10μs, 100% CPU)
    /// </summary>
    BusySpin,

    /// <summary>
    /// Block with timeout (deadlock detection)
    /// </summary>
    TimeoutBlocking
}

/// <summary>
/// Producer mode for RingBuffer
/// </summary>
public enum ProducerMode
{
    /// <summary>
    /// Single producer (faster, no CAS operations)
    /// </summary>
    Single,

    /// <summary>
    /// Multiple producers (CAS-based coordination)
    /// </summary>
    Multi
}
```

**Configuration API**:

```csharp
// File: src/HeroMessaging/Extensions/InMemoryQueueServiceCollectionExtensions.cs

namespace HeroMessaging.Extensions;

public static class InMemoryQueueServiceCollectionExtensions
{
    public static IServiceCollection AddHeroMessaging(
        this IServiceCollection services,
        Action<HeroMessagingOptionsBuilder> configure)
    {
        var builder = new HeroMessagingOptionsBuilder(services);
        configure(builder);
        return services;
    }
}

public class HeroMessagingOptionsBuilder
{
    private readonly IServiceCollection _services;

    public HeroMessagingOptionsBuilder(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>
    /// Configure in-memory queue options
    /// </summary>
    public HeroMessagingOptionsBuilder UseInMemoryQueue(
        Action<InMemoryQueueOptionsBuilder> configure)
    {
        var options = new InMemoryQueueOptions();
        var builder = new InMemoryQueueOptionsBuilder(options);
        configure(builder);

        _services.AddSingleton(options);
        return this;
    }
}

public class InMemoryQueueOptionsBuilder
{
    private readonly InMemoryQueueOptions _options;

    public InMemoryQueueOptionsBuilder(InMemoryQueueOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Use Channel-based implementation (default)
    /// </summary>
    public InMemoryQueueOptionsBuilder UseChannelMode()
    {
        _options.Mode = QueueMode.Channel;
        return this;
    }

    /// <summary>
    /// Use RingBuffer-based implementation (high performance)
    /// </summary>
    public InMemoryQueueOptionsBuilder UseRingBufferMode()
    {
        _options.Mode = QueueMode.RingBuffer;
        return this;
    }

    /// <summary>
    /// Set buffer size (must be power of 2 for RingBuffer mode)
    /// </summary>
    public InMemoryQueueOptionsBuilder WithBufferSize(int size)
    {
        if (_options.Mode == QueueMode.RingBuffer && !IsPowerOf2(size))
        {
            throw new ArgumentException(
                "Buffer size must be power of 2 when using RingBuffer mode",
                nameof(size));
        }

        _options.BufferSize = size;
        return this;
    }

    /// <summary>
    /// Set wait strategy (RingBuffer mode only)
    /// </summary>
    public InMemoryQueueOptionsBuilder WithWaitStrategy(WaitStrategy strategy)
    {
        _options.WaitStrategy = strategy;
        return this;
    }

    /// <summary>
    /// Set producer mode (RingBuffer mode only)
    /// </summary>
    public InMemoryQueueOptionsBuilder WithProducerMode(ProducerMode mode)
    {
        _options.ProducerMode = mode;
        return this;
    }

    /// <summary>
    /// Drop oldest messages when buffer is full (Channel mode only)
    /// </summary>
    public InMemoryQueueOptionsBuilder DropWhenFull(bool drop = true)
    {
        _options.DropWhenFull = drop;
        return this;
    }

    private static bool IsPowerOf2(int value)
    {
        return value > 0 && (value & (value - 1)) == 0;
    }
}
```

**Usage Examples**:

```csharp
// Example 1: Use default Channel mode (existing behavior)
services.AddHeroMessaging(builder =>
{
    builder.UseInMemoryQueue(queue =>
    {
        queue.UseChannelMode()
             .WithBufferSize(1024)
             .DropWhenFull(false);
    });
});

// Example 2: Use RingBuffer mode for high performance
services.AddHeroMessaging(builder =>
{
    builder.UseInMemoryQueue(queue =>
    {
        queue.UseRingBufferMode()
             .WithBufferSize(4096)              // Must be power of 2
             .WithWaitStrategy(WaitStrategy.Sleeping)
             .WithProducerMode(ProducerMode.Multi);
    });
});

// Example 3: Ultra-low latency configuration
services.AddHeroMessaging(builder =>
{
    builder.UseInMemoryQueue(queue =>
    {
        queue.UseRingBufferMode()
             .WithBufferSize(8192)
             .WithWaitStrategy(WaitStrategy.BusySpin)  // <10μs latency, 100% CPU
             .WithProducerMode(ProducerMode.Single);   // Single producer optimization
    });
});
```

---

### 2.2 RingBuffer-Based InMemoryQueue

**File**: `src/HeroMessaging/Transport/InMemory/RingBufferQueue.cs`

**Goal**: RingBuffer implementation that implements the same interface as Channel-based queue

```csharp
namespace HeroMessaging.Transport.InMemory;

/// <summary>
/// High-performance in-memory queue using lock-free ring buffer.
/// Provides 20x lower latency and 5x higher throughput vs Channel-based implementation.
/// </summary>
internal class RingBufferQueue : IDisposable, IAsyncDisposable
{
    private readonly RingBuffer<MessageEvent> _ringBuffer;
    private readonly List<InMemoryConsumer> _consumers = new();
    private readonly List<IEventProcessor> _processors = new();
    private readonly CancellationTokenSource _cts = new();
    private long _messageCount;
    private long _depth;

    // Message wrapper for ring buffer
    private class MessageEvent
    {
        public TransportEnvelope? Envelope { get; set; }
    }

    private class MessageEventFactory : IEventFactory<MessageEvent>
    {
        public MessageEvent Create() => new();
    }

    public long MessageCount => Interlocked.Read(ref _messageCount);
    public long Depth => Interlocked.Read(ref _depth);

    public RingBufferQueue(InMemoryQueueOptions options)
    {
        // Validate configuration
        if (!IsPowerOf2(options.BufferSize))
        {
            throw new ArgumentException(
                "Buffer size must be power of 2 for RingBuffer mode",
                nameof(options.BufferSize));
        }

        // Create wait strategy
        var waitStrategy = CreateWaitStrategy(options.WaitStrategy);

        // Create ring buffer
        var eventFactory = new MessageEventFactory();
        var producerType = options.ProducerMode == ProducerMode.Single
            ? ProducerType.Single
            : ProducerType.Multi;

        _ringBuffer = new RingBuffer<MessageEvent>(
            options.BufferSize,
            eventFactory,
            producerType,
            waitStrategy);
    }

    public ValueTask<bool> EnqueueAsync(
        TransportEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Two-phase commit: claim sequence, write data, publish
            long sequence = _ringBuffer.Next();

            try
            {
                MessageEvent evt = _ringBuffer.Get(sequence);
                evt.Envelope = envelope;

                Interlocked.Increment(ref _messageCount);
                Interlocked.Increment(ref _depth);
            }
            finally
            {
                _ringBuffer.Publish(sequence);
            }

            return new ValueTask<bool>(true);
        }
        catch (Exception)
        {
            return new ValueTask<bool>(false);
        }
    }

    public void AddConsumer(InMemoryConsumer consumer)
    {
        _consumers.Add(consumer);

        // Create event processor for this consumer
        var handler = new ConsumerEventHandler(consumer, this);
        var barrier = _ringBuffer.NewBarrier();
        var processor = new BatchEventProcessor<MessageEvent>(
            _ringBuffer,
            barrier,
            handler);

        _processors.Add(processor);
        processor.Start();

        // Add processor sequence as gating sequence for backpressure
        _ringBuffer.AddGatingSequence(processor.Sequence);
    }

    public void RemoveConsumer(InMemoryConsumer consumer)
    {
        _consumers.Remove(consumer);
        // Note: Processor cleanup would happen here
    }

    private class ConsumerEventHandler : IEventHandler<MessageEvent>
    {
        private readonly InMemoryConsumer _consumer;
        private readonly RingBufferQueue _queue;

        public ConsumerEventHandler(InMemoryConsumer consumer, RingBufferQueue queue)
        {
            _consumer = consumer;
            _queue = queue;
        }

        public void OnEvent(MessageEvent evt, long sequence, bool endOfBatch)
        {
            if (evt.Envelope != null)
            {
                try
                {
                    // Process message (synchronous to avoid allocation)
                    _consumer.DeliverMessageAsync(evt.Envelope, default).AsTask().Wait();
                    Interlocked.Decrement(ref _queue._depth);
                }
                catch (Exception)
                {
                    // Log error (would use ILogger in real implementation)
                }
                finally
                {
                    evt.Envelope = null; // Clear for reuse (zero allocation)
                }
            }
        }

        public void OnError(Exception ex)
        {
            // Log error
        }

        public void OnShutdown()
        {
            // Cleanup
        }
    }

    private static IWaitStrategy CreateWaitStrategy(WaitStrategy strategy)
    {
        return strategy switch
        {
            WaitStrategy.Blocking => new BlockingWaitStrategy(),
            WaitStrategy.Sleeping => new SleepingWaitStrategy(),
            WaitStrategy.Yielding => new YieldingWaitStrategy(),
            WaitStrategy.BusySpin => new BusySpinWaitStrategy(),
            WaitStrategy.TimeoutBlocking => new TimeoutBlockingWaitStrategy(TimeSpan.FromSeconds(30)),
            _ => new SleepingWaitStrategy()
        };
    }

    private static bool IsPowerOf2(int value)
    {
        return value > 0 && (value & (value - 1)) == 0;
    }

    public async ValueTask DisposeAsync()
    {
        // Stop all processors
        foreach (var processor in _processors)
        {
            processor.Stop();
            processor.Dispose();
        }

        _cts.Cancel();
        _cts.Dispose();
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
```

**Factory Pattern for Queue Creation**:

```csharp
// File: src/HeroMessaging/Transport/InMemory/InMemoryQueueFactory.cs

namespace HeroMessaging.Transport.InMemory;

internal static class InMemoryQueueFactory
{
    public static object CreateQueue(InMemoryQueueOptions options)
    {
        return options.Mode switch
        {
            QueueMode.Channel => new InMemoryQueue(
                options.BufferSize,
                options.DropWhenFull),

            QueueMode.RingBuffer => new RingBufferQueue(options),

            _ => throw new ArgumentException($"Unknown queue mode: {options.Mode}")
        };
    }
}
```

**Migration Path**:
1. **Default Behavior**: Channel-based (no breaking changes)
2. **Opt-In**: Users explicitly configure RingBuffer mode
3. **Feature Flag**: Easy toggle between modes for A/B testing
4. **Gradual Rollout**: Monitor performance, collect metrics
5. **Future Default**: After 6-12 months, consider making RingBuffer default

**Testing Requirements**:
- Drop-in replacement tests: Verify identical behavior to Channel-based queue
- Performance tests: Confirm 20x latency improvement
- Load tests: 500K+ messages/second sustained throughput
- Concurrency tests: 100+ concurrent producers/consumers
- Configuration tests: Verify all mode combinations work correctly

---

### 2.3 RingBuffer-Based EventBus

**File**: `src/HeroMessaging/Processing/RingBufferEventBus.cs`

**Goal**: Replace TPL Dataflow ActionBlock with ring buffer for lock-free event distribution

```csharp
namespace HeroMessaging.Processing;

/// <summary>
/// Lock-free event bus implementation using ring buffer.
/// Eliminates ActionBlock overhead for 10x+ throughput improvement.
/// </summary>
public class RingBufferEventBus : IEventBus, IProcessor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RingBufferEventBus> _logger;
    private readonly RingBuffer<EventEnvelope> _ringBuffer;
    private readonly MessageProcessingPipelineBuilder _pipelineBuilder;
    private readonly List<IEventProcessor> _processors = new();

    private long _publishedCount;
    private long _failedCount;
    private int _registeredHandlers;

    public RingBufferEventBus(
        IServiceProvider serviceProvider,
        EventBusOptions options,
        ILogger<RingBufferEventBus>? logger = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger ?? NullLogger<RingBufferEventBus>.Instance;
        _pipelineBuilder = new MessageProcessingPipelineBuilder(serviceProvider);

        ConfigurePipeline();

        // Create ring buffer
        var eventFactory = new EventEnvelopeFactory();
        var waitStrategy = CreateWaitStrategy(options.WaitStrategy);

        _ringBuffer = new RingBuffer<EventEnvelope>(
            options.BufferSize,
            eventFactory,
            ProducerType.Multi,  // Multiple threads publish events
            waitStrategy);

        // Create processors (one per CPU core for parallelism)
        for (int i = 0; i < Environment.ProcessorCount; i++)
        {
            var handler = new EventProcessingHandler(this);
            var barrier = _ringBuffer.NewBarrier();
            var processor = new BatchEventProcessor<EventEnvelope>(
                _ringBuffer,
                barrier,
                handler);

            _processors.Add(processor);
            processor.Start();

            // Add as gating sequence for backpressure
            _ringBuffer.AddGatingSequence(processor.Sequence);
        }
    }

    public bool IsRunning { get; private set; } = true;

    private void ConfigurePipeline()
    {
        _pipelineBuilder
            .UseMetrics()           // Outermost - collect metrics for everything
            .UseLogging()           // Log the entire process
            .UseCorrelation()       // Track correlation/causation for choreography
            .UseValidation()        // Validate before processing
            .UseErrorHandling()     // Handle errors with dead letter queue
            .UseRetry();            // Innermost - retry the actual processing
    }

    public async Task Publish(IEvent @event, CancellationToken cancellationToken = default)
    {
        var eventType = @event.GetType();
        var handlerType = typeof(IEventHandler<>).MakeGenericType(eventType);
        var handlers = _serviceProvider.GetServices(handlerType).ToList();

        if (!handlers.Any())
        {
            _logger.LogDebug("No handlers found for event type {EventType}", eventType.Name);
            return;
        }

        Interlocked.Add(ref _publishedCount, handlers.Count);
        _registeredHandlers = handlers.Count;

        // Publish each handler invocation to ring buffer
        foreach (var handler in handlers)
        {
            long sequence = _ringBuffer.Next();

            try
            {
                var envelope = _ringBuffer.Get(sequence);
                envelope.Event = @event;
                envelope.Handler = handler!;
                envelope.HandlerType = handlerType;
                envelope.CancellationToken = cancellationToken;
            }
            finally
            {
                _ringBuffer.Publish(sequence);
            }
        }
    }

    private class EventProcessingHandler : IEventHandler<EventEnvelope>
    {
        private readonly RingBufferEventBus _eventBus;

        public EventProcessingHandler(RingBufferEventBus eventBus)
        {
            _eventBus = eventBus;
        }

        public void OnEvent(EventEnvelope envelope, long sequence, bool endOfBatch)
        {
            if (envelope.Event == null) return;

            // Process through pipeline
            _eventBus.ProcessEventWithPipelineSync(envelope);

            // Clear for reuse (zero allocation)
            envelope.Event = null;
            envelope.Handler = null;
        }

        public void OnError(Exception ex)
        {
            _eventBus._logger.LogError(ex, "Error processing event");
        }

        public void OnShutdown()
        {
            // Cleanup
        }
    }

    private void ProcessEventWithPipelineSync(EventEnvelope envelope)
    {
        // Create the core processor that executes the handler
        var coreProcessor = new CoreMessageProcessor(async (message, context, ct) =>
        {
            var handleMethod = envelope.HandlerType!.GetMethod("Handle");
            if (handleMethod == null)
            {
                throw new InvalidOperationException(
                    $"Handle method not found on {envelope.HandlerType.Name}");
            }

            await (Task)handleMethod.Invoke(envelope.Handler, [envelope.Event, ct])!;
        });

        // Build the pipeline
        var pipeline = _pipelineBuilder.Build(coreProcessor);

        // Create processing context
        var context = new ProcessingContext
        {
            Component = "EventBus",
            Handler = envelope.Handler,
            HandlerType = envelope.HandlerType
        }
        .WithMetadata("EventType", envelope.Event!.GetType().Name)
        .WithMetadata("HandlerType", envelope.Handler!.GetType().Name);

        // Process through the pipeline (synchronous to avoid allocation)
        var result = pipeline.ProcessAsync(
            envelope.Event,
            context,
            envelope.CancellationToken).GetAwaiter().GetResult();

        if (!result.Success && result.Exception != null)
        {
            Interlocked.Increment(ref _failedCount);

            _logger.LogError(result.Exception,
                "Failed to process event {EventType} with handler {HandlerType}: {Message}",
                envelope.Event.GetType().Name,
                envelope.Handler.GetType().Name,
                result.Message);
        }
    }

    public IEventBusMetrics GetMetrics()
    {
        return new EventBusMetrics
        {
            PublishedCount = _publishedCount,
            FailedCount = _failedCount,
            RegisteredHandlers = _registeredHandlers
        };
    }

    private class EventEnvelope
    {
        public IEvent? Event { get; set; }
        public object? Handler { get; set; }
        public Type? HandlerType { get; set; }
        public CancellationToken CancellationToken { get; set; }
    }

    private class EventEnvelopeFactory : IEventFactory<EventEnvelope>
    {
        public EventEnvelope Create() => new();
    }

    private static IWaitStrategy CreateWaitStrategy(WaitStrategy strategy)
    {
        return strategy switch
        {
            WaitStrategy.Blocking => new BlockingWaitStrategy(),
            WaitStrategy.Sleeping => new SleepingWaitStrategy(),
            WaitStrategy.Yielding => new YieldingWaitStrategy(),
            WaitStrategy.BusySpin => new BusySpinWaitStrategy(),
            WaitStrategy.TimeoutBlocking => new TimeoutBlockingWaitStrategy(TimeSpan.FromSeconds(30)),
            _ => new SleepingWaitStrategy()
        };
    }

    private class EventBusMetrics : IEventBusMetrics
    {
        public long PublishedCount { get; init; }
        public long FailedCount { get; init; }
        public int RegisteredHandlers { get; init; }
    }
}
```

**Expected Performance**:
- **Throughput**: 500K+ events/sec (vs 50K with ActionBlock)
- **Latency**: <50μs event dispatch (vs 500μs with ActionBlock)
- **Allocations**: 0 bytes steady state (vs ~1KB per event)

**Testing Requirements**:
- Compatibility tests: Verify all existing EventBus tests pass
- Performance benchmarks: Compare against ActionBlock implementation
- Load tests: 1M+ events with 100+ handlers
- Pipeline integration: Verify all decorators work correctly

---

## Phase 3: Advanced Features (3 weeks, 120 hours)

### 3.1 Sequence Barriers and Dependencies

**File**: `src/HeroMessaging.RingBuffer/SequenceBarrier.cs`

**Capabilities**:
- Create processing stages with dependencies
- Ensure Event A is processed before Event B
- Multi-cast: Multiple handlers process same event
- Diamond dependencies: Complex processing graphs

```csharp
namespace HeroMessaging.RingBuffer;

/// <summary>
/// Coordinates dependencies between event processors
/// </summary>
public sealed class SequenceBarrier : ISequenceBarrier
{
    private readonly IWaitStrategy _waitStrategy;
    private readonly ISequence _cursorSequence;
    private readonly ISequence[] _dependentSequences;
    private volatile bool _alerted;

    public SequenceBarrier(
        ISequence cursorSequence,
        IWaitStrategy waitStrategy,
        params ISequence[] dependentSequences)
    {
        _cursorSequence = cursorSequence;
        _waitStrategy = waitStrategy;
        _dependentSequences = dependentSequences;
    }

    public long WaitFor(long sequence)
    {
        CheckAlert();

        // Wait for the cursor (ring buffer) to advance
        long availableSequence = _waitStrategy.WaitFor(sequence);

        // Also wait for all dependent processors
        if (_dependentSequences.Length > 0)
        {
            long minSequence = long.MaxValue;
            foreach (var dependent in _dependentSequences)
            {
                long depSeq = dependent.Value;
                if (depSeq < minSequence)
                    minSequence = depSeq;
            }

            // Can't proceed past the slowest dependent
            if (minSequence < availableSequence)
                availableSequence = minSequence;
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
            throw new AlertException();
    }
}

public class AlertException : Exception
{
    public AlertException() : base("Sequence barrier alerted") { }
}
```

**Use Cases**:

```csharp
// Example: Event validation -> Processing -> Logging
// Logging must happen AFTER processing

var validationProcessor = new BatchEventProcessor<Event>(ringBuffer, barrier1, validator);
var processingProcessor = new BatchEventProcessor<Event>(ringBuffer, barrier2, processor);

// Create barrier that waits for validation
var barrier2 = ringBuffer.NewBarrier(validationProcessor.Sequence);

// Create barrier that waits for processing
var loggingBarrier = ringBuffer.NewBarrier(processingProcessor.Sequence);
var loggingProcessor = new BatchEventProcessor<Event>(ringBuffer, loggingBarrier, logger);
```

---

### 3.2 Object Pooling and Zero-Allocation Paths

**File**: `src/HeroMessaging.RingBuffer/EventTranslator.cs`

**Goal**: Eliminate all allocations in hot path

```csharp
namespace HeroMessaging.RingBuffer;

/// <summary>
/// Translates input data into ring buffer events without allocation
/// </summary>
public interface IEventTranslator<T>
{
    void TranslateTo(T evt, long sequence);
}

/// <summary>
/// Translates input with one argument
/// </summary>
public interface IEventTranslatorOneArg<T, A>
{
    void TranslateTo(T evt, long sequence, A arg0);
}

/// <summary>
/// Translates input with two arguments
/// </summary>
public interface IEventTranslatorTwoArg<T, A, B>
{
    void TranslateTo(T evt, long sequence, A arg0, B arg1);
}

/// <summary>
/// Ring buffer extensions for zero-allocation publishing
/// </summary>
public static class RingBufferExtensions
{
    /// <summary>
    /// Publish using translator (zero allocation)
    /// </summary>
    public static void PublishEvent<T>(
        this RingBuffer<T> ringBuffer,
        IEventTranslator<T> translator) where T : class
    {
        long sequence = ringBuffer.Next();
        try
        {
            translator.TranslateTo(ringBuffer.Get(sequence), sequence);
        }
        finally
        {
            ringBuffer.Publish(sequence);
        }
    }

    /// <summary>
    /// Publish with one argument (zero allocation)
    /// </summary>
    public static void PublishEvent<T, A>(
        this RingBuffer<T> ringBuffer,
        IEventTranslatorOneArg<T, A> translator,
        A arg0) where T : class
    {
        long sequence = ringBuffer.Next();
        try
        {
            translator.TranslateTo(ringBuffer.Get(sequence), sequence, arg0);
        }
        finally
        {
            ringBuffer.Publish(sequence);
        }
    }

    /// <summary>
    /// Publish batch of events (zero allocation)
    /// </summary>
    public static void PublishEvents<T>(
        this RingBuffer<T> ringBuffer,
        IEventTranslator<T>[] translators) where T : class
    {
        int batchSize = translators.Length;
        long hi = ringBuffer.Next(batchSize);
        long lo = hi - (batchSize - 1);

        try
        {
            for (long i = lo; i <= hi; i++)
            {
                var translator = translators[i - lo];
                translator.TranslateTo(ringBuffer.Get(i), i);
            }
        }
        finally
        {
            ringBuffer.Publish(lo, hi);
        }
    }
}
```

---

## Phase 4: Performance Optimization (2 weeks, 80 hours)

### 4.1 CPU Cache Optimization

**Techniques**:

1. **False Sharing Prevention**:
```csharp
// Bad: Fields on same cache line
class BadExample
{
    private long _producerSequence;  // Cache line 0
    private long _consumerSequence;  // Cache line 0 - FALSE SHARING!
}

// Good: Padded to separate cache lines
[StructLayout(LayoutKind.Explicit, Size = 128)]
struct CachePaddedLong
{
    [FieldOffset(56)]
    public long Value;
}

class GoodExample
{
    private CachePaddedLong _producerSequence;  // Cache line 0
    private CachePaddedLong _consumerSequence;  // Cache line 2
}
```

2. **Sequential Memory Access**:
```csharp
// Ring buffer ensures sequential access
// CPU prefetcher can predict next access
T[] _entries = new T[1024];
int index = sequence & _bufferMask;
T evt = _entries[index]; // Prefetcher loads next entries
```

3. **Branch Prediction Optimization**:
```csharp
// Bad: Unpredictable branch
if (sequence % 100 == 0) { ... }

// Good: Predictable branch (always same direction)
if (sequence > endOfBatch) { ... }
```

**Performance Targets**:
- L1 cache hit rate: >99%
- L2 cache hit rate: >95%
- Branch prediction accuracy: >98%
- CPU cache misses: <1 per 1000 messages

---

### 4.2 Benchmarking Infrastructure

**File**: `tests/HeroMessaging.RingBuffer.Benchmarks/RingBufferBenchmarks.cs`

```csharp
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnostics.Windows.Configs;

namespace HeroMessaging.RingBuffer.Benchmarks;

[MemoryDiagnoser]
[ThreadingDiagnoser]
[EtwProfiler] // CPU cache analysis
public class QueueModeBenchmarks
{
    private RingBufferQueue _ringBufferQueue = null!;
    private InMemoryQueue _channelQueue = null!;

    [Params(1024, 4096, 16384)]
    public int BufferSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Setup ring buffer queue
        var ringBufferOptions = new InMemoryQueueOptions
        {
            Mode = QueueMode.RingBuffer,
            BufferSize = BufferSize,
            WaitStrategy = WaitStrategy.Sleeping,
            ProducerMode = ProducerMode.Single
        };
        _ringBufferQueue = new RingBufferQueue(ringBufferOptions);

        // Setup channel queue
        var channelOptions = new InMemoryQueueOptions
        {
            Mode = QueueMode.Channel,
            BufferSize = BufferSize,
            DropWhenFull = false
        };
        _channelQueue = new InMemoryQueue(BufferSize, false);
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Latency")]
    public void Channel_SingleMessage()
    {
        var envelope = new TransportEnvelope();
        _channelQueue.EnqueueAsync(envelope).AsTask().Wait();
    }

    [Benchmark]
    [BenchmarkCategory("Latency")]
    public void RingBuffer_SingleMessage()
    {
        var envelope = new TransportEnvelope();
        _ringBufferQueue.EnqueueAsync(envelope).AsTask().Wait();
    }

    [Benchmark]
    [BenchmarkCategory("Throughput")]
    public void RingBuffer_Throughput_1M_Messages()
    {
        for (int i = 0; i < 1_000_000; i++)
        {
            var envelope = new TransportEnvelope();
            _ringBufferQueue.EnqueueAsync(envelope).AsTask().Wait();
        }
    }
}
```

**Benchmark Categories**:
1. **Latency**: p50, p99, p99.9 measurements
2. **Throughput**: Messages per second
3. **Memory**: Allocations per operation
4. **CPU**: Cache misses, branch mispredictions
5. **Scalability**: 1, 2, 4, 8, 16 producers/consumers

**Performance Gates** (CI must pass):
- Latency: <50μs p99 (vs 1ms baseline)
- Throughput: >500K msg/s (vs 100K baseline)
- Allocations: 0 bytes (vs 200 bytes baseline)
- Regression: <10% degradation from previous baseline

---

## Phase 5: Production Readiness (2 weeks, 80 hours)

### 5.1 Observability and Diagnostics

**File**: `src/HeroMessaging.RingBuffer/Diagnostics/RingBufferMetrics.cs`

```csharp
namespace HeroMessaging.RingBuffer.Diagnostics;

/// <summary>
/// Metrics for ring buffer performance
/// </summary>
public interface IRingBufferMetrics
{
    /// <summary>
    /// Current sequence number being published
    /// </summary>
    long CurrentSequence { get; }

    /// <summary>
    /// Number of slots remaining in buffer
    /// </summary>
    long RemainingCapacity { get; }

    /// <summary>
    /// Minimum sequence across all consumers
    /// </summary>
    long MinimumGatingSequence { get; }

    /// <summary>
    /// Number of events published per second
    /// </summary>
    long PublishRate { get; }

    /// <summary>
    /// Number of events consumed per second
    /// </summary>
    long ConsumeRate { get; }

    /// <summary>
    /// Current buffer utilization (0.0 - 1.0)
    /// </summary>
    double BufferUtilization { get; }
}

/// <summary>
/// Diagnostic event handler that tracks performance
/// </summary>
public class DiagnosticEventHandler<T> : IEventHandler<T>
{
    private readonly IEventHandler<T> _innerHandler;
    private readonly ILogger _logger;
    private long _eventsProcessed;
    private long _errorsEncountered;
    private readonly Stopwatch _sw = Stopwatch.StartNew();

    public DiagnosticEventHandler(IEventHandler<T> innerHandler, ILogger logger)
    {
        _innerHandler = innerHandler;
        _logger = logger;
    }

    public void OnEvent(T data, long sequence, bool endOfBatch)
    {
        try
        {
            _innerHandler.OnEvent(data, sequence, endOfBatch);
            Interlocked.Increment(ref _eventsProcessed);

            // Log metrics every 10K events
            if (_eventsProcessed % 10_000 == 0)
            {
                LogMetrics();
            }
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _errorsEncountered);
            _logger.LogError(ex, "Error processing event at sequence {Sequence}", sequence);
            throw;
        }
    }

    private void LogMetrics()
    {
        var elapsed = _sw.Elapsed;
        var rate = _eventsProcessed / elapsed.TotalSeconds;

        _logger.LogInformation(
            "RingBuffer Performance: {EventsProcessed:N0} events, " +
            "{Rate:N0} events/sec, {Errors:N0} errors",
            _eventsProcessed, rate, _errorsEncountered);
    }

    public void OnError(Exception ex)
    {
        _innerHandler.OnError(ex);
    }

    public void OnShutdown()
    {
        LogMetrics();
        _innerHandler.OnShutdown();
    }
}
```

---

### 5.2 Documentation

**Files to Create**:

1. **Architecture Decision Record**:
   - `docs/adr/0006-ringbuffer-implementation.md`
   - Rationale for in-house vs library
   - Performance characteristics
   - Trade-offs and limitations

2. **Developer Guide**:
   - `docs/ringbuffer-developer-guide.md`
   - How to use RingBuffer components
   - When to use which wait strategy
   - Performance tuning guide

3. **API Documentation**:
   - XML comments on all public APIs
   - Code examples for common scenarios
   - Performance best practices

4. **Migration Guide**:
   - `docs/migration-to-ringbuffer.md`
   - Step-by-step migration from Channel mode
   - Configuration examples
   - Rollback procedures

---

## Project Structure

```
src/HeroMessaging.RingBuffer/
├── RingBuffer.cs                    # Core ring buffer implementation
├── Sequences/
│   ├── ISequence.cs                 # Sequence interface
│   ├── Sequence.cs                  # Padded sequence implementation
│   └── SequenceBarrier.cs           # Dependency coordination
├── Sequencers/
│   ├── Sequencer.cs                 # Base sequencer
│   ├── SingleProducerSequencer.cs   # Optimized single producer
│   └── MultiProducerSequencer.cs    # CAS-based multi producer
├── WaitStrategies/
│   ├── IWaitStrategy.cs             # Wait strategy interface
│   ├── BlockingWaitStrategy.cs      # Lock-based waiting
│   ├── SleepingWaitStrategy.cs      # Progressive backoff
│   ├── YieldingWaitStrategy.cs      # Yielding strategy
│   ├── BusySpinWaitStrategy.cs      # Busy spin (ultra-low latency)
│   └── TimeoutBlockingWaitStrategy.cs
├── EventProcessors/
│   ├── IEventProcessor.cs           # Event processor interface
│   ├── BatchEventProcessor.cs       # Batch processing
│   └── WorkProcessor.cs             # Work-stealing processor
├── EventHandlers/
│   └── IEventHandler.cs             # Event handler interface
├── EventFactories/
│   └── IEventFactory.cs             # Pre-allocation factory
├── EventTranslators/
│   ├── IEventTranslator.cs          # Zero-allocation publishing
│   ├── IEventTranslatorOneArg.cs
│   └── IEventTranslatorTwoArg.cs
├── Diagnostics/
│   ├── RingBufferMetrics.cs         # Performance metrics
│   └── DiagnosticEventHandler.cs
└── Extensions/
    └── RingBufferExtensions.cs      # Convenience methods

src/HeroMessaging.Abstractions/
├── Transport/
│   ├── InMemoryQueueOptions.cs      # Configuration options
│   ├── QueueMode.cs                 # Channel vs RingBuffer
│   ├── WaitStrategy.cs              # Wait strategy enum
│   └── ProducerMode.cs              # Single vs Multi

src/HeroMessaging/
├── Transport/InMemory/
│   ├── InMemoryQueue.cs             # Existing Channel-based (DEFAULT)
│   ├── RingBufferQueue.cs           # New RingBuffer-based (OPT-IN)
│   └── InMemoryQueueFactory.cs      # Factory pattern
├── Processing/
│   ├── EventBus.cs                  # Existing ActionBlock-based
│   └── RingBufferEventBus.cs        # New RingBuffer-based
└── Extensions/
    └── InMemoryQueueServiceCollectionExtensions.cs

tests/HeroMessaging.RingBuffer.Tests/
├── Unit/
│   ├── RingBufferTests.cs
│   ├── SequencerTests.cs
│   ├── WaitStrategyTests.cs
│   └── EventProcessorTests.cs
├── Integration/
│   ├── EndToEndTests.cs
│   ├── PipelineIntegrationTests.cs
│   └── MigrationTests.cs
└── Performance/
    └── PerformanceRegressionTests.cs

tests/HeroMessaging.RingBuffer.Benchmarks/
├── LatencyBenchmarks.cs
├── ThroughputBenchmarks.cs
├── MemoryBenchmarks.cs
├── CpuCacheBenchmarks.cs
└── QueueModeComparisonBenchmarks.cs

docs/
├── adr/
│   └── 0006-ringbuffer-implementation.md
├── ringbuffer-developer-guide.md
├── ringbuffer-performance-tuning.md
└── migration-to-ringbuffer.md
```

---

## Implementation Timeline

### Week 1-2: Ring Buffer Core
- [ ] RingBuffer<T> implementation
- [ ] Power-of-2 validation
- [ ] Sequence management
- [ ] Pre-allocation logic
- [ ] Unit tests (50+)

### Week 3-4: Sequencers
- [ ] Sequencer base class
- [ ] SingleProducerSequencer
- [ ] MultiProducerSequencer
- [ ] Gating sequence coordination
- [ ] Unit tests (40+)

### Week 5: Wait Strategies
- [ ] IWaitStrategy interface
- [ ] 5 wait strategy implementations
- [ ] Performance benchmarks
- [ ] Unit tests (30+)

### Week 6: Event Processors
- [ ] IEventProcessor interface
- [ ] BatchEventProcessor
- [ ] WorkProcessor
- [ ] Exception handling
- [ ] Unit tests (40+)

### Week 7-8: HeroMessaging Integration
- [ ] InMemoryQueueOptions with QueueMode
- [ ] RingBufferQueue implementation
- [ ] RingBufferEventBus implementation
- [ ] Factory pattern for queue creation
- [ ] Feature flags and configuration
- [ ] Integration tests (40+)

### Week 9-10: Advanced Features
- [ ] SequenceBarrier implementation
- [ ] Dependency chains
- [ ] EventTranslator patterns
- [ ] Zero-allocation paths
- [ ] Performance tests

### Week 11-12: Performance Optimization
- [ ] Cache line padding
- [ ] False sharing elimination
- [ ] Branch prediction optimization
- [ ] Benchmark suite
- [ ] Performance regression tests
- [ ] CI performance gates

### Week 13-14: Production Readiness
- [ ] Configuration API refinement
- [ ] Fluent builders
- [ ] Observability metrics
- [ ] Diagnostic handlers
- [ ] Documentation (ADR, guides, API docs)
- [ ] Migration guide

### Week 15-16: Testing and Polish
- [ ] Full test suite execution
- [ ] Cross-platform testing
- [ ] Load testing (1M+ messages)
- [ ] Endurance testing (24+ hours)
- [ ] Documentation review
- [ ] Code review and refinement

---

## Success Criteria

### Performance Targets (Must Achieve)

✅ **Latency**: <50μs p99 (20x improvement over Channel)
✅ **Throughput**: >500K msg/s (5x improvement over Channel)
✅ **Allocations**: 0 bytes in hot path (100% reduction)
✅ **CPU Cache Misses**: <1 per 1000 messages (10x improvement)

### Quality Targets (Must Achieve)

✅ **Test Coverage**: 80%+ overall, 100% public APIs
✅ **Test Count**: 200+ unit tests, 40+ integration tests
✅ **Documentation**: 100% XML docs on public APIs
✅ **Constitutional Compliance**: All principles upheld

### Operational Targets (Must Achieve)

✅ **Backward Compatibility**: 100% feature parity with Channel mode
✅ **Default Behavior**: Channel mode remains default (no breaking changes)
✅ **Feature Flags**: Instant rollback capability via QueueMode configuration
✅ **Multi-Framework**: netstandard2.0, net8.0, net9.0, net10.0
✅ **Cross-Platform**: Windows, Linux, macOS, ARM64

---

## Risk Mitigation

### Technical Risks

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Performance regression | Low | High | Comprehensive benchmarks, CI gates |
| Memory leaks | Medium | High | Long-running tests, memory profiling |
| Thread safety bugs | Medium | High | Concurrency tests, stress testing |
| Buffer overflow | Low | High | Power-of-2 validation, wraparound tests |
| Breaking changes | Very Low | High | Keep Channel as default, opt-in RingBuffer |

### Operational Risks

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Learning curve | Medium | Medium | Comprehensive docs, examples |
| Configuration errors | Medium | Medium | Validation, sensible defaults |
| Production issues | Low | High | Gradual rollout, instant rollback via Mode |
| Support burden | Medium | Low | Self-service docs, diagnostics |

---

## Conclusion

This comprehensive plan provides a roadmap for building high-performance ring buffer features in-house for HeroMessaging. By implementing these patterns natively, we achieve:

1. **Zero external dependencies**
2. **20x latency improvement** (<1ms → <50μs)
3. **5x throughput increase** (100K → 500K+ msg/s)
4. **Zero allocations** in steady state
5. **Full control** over implementation and optimization
6. **Seamless integration** with existing HeroMessaging architecture
7. **Zero breaking changes** - Channel mode remains default, RingBuffer is opt-in

### Configuration Philosophy

Users choose between two modes via `QueueMode`:

- **QueueMode.Channel** (default): Async/await based, flexible backpressure, existing behavior
- **QueueMode.RingBuffer** (opt-in): Lock-free, zero-allocation, ultra-low latency

This approach ensures:
- No breaking changes to existing users
- Easy A/B testing and performance comparison
- Instant rollback if issues arise
- Gradual migration path

The phased approach ensures incremental value delivery while maintaining constitutional compliance with testing excellence, code quality, and performance standards.

---

**Next Steps**: Review this plan, adjust priorities as needed, and begin Phase 1 implementation.
