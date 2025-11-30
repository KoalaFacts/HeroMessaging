using HeroMessaging.RingBuffer.EventHandlers;
using HeroMessaging.RingBuffer.Sequences;

namespace HeroMessaging.RingBuffer.EventProcessors;

/// <summary>
/// Batch event processor for high-throughput scenarios.
/// Processes events in batches to maximize cache efficiency and throughput.
/// Thread-safe for Start/Stop operations.
/// </summary>
/// <typeparam name="T">The type of event to process</typeparam>
public sealed class BatchEventProcessor<T> : IEventProcessor, IAsyncDisposable where T : class
{
    private readonly RingBuffer<T> _ringBuffer;
    private readonly ISequenceBarrier _sequenceBarrier;
    private readonly IEventHandler<T> _eventHandler;
    private readonly CancellationTokenSource _cts = new();
    private Task? _processingTask;
    private int _isRunning; // 0 = stopped, 1 = running (for Interlocked)
    private int _disposed;  // 0 = not disposed, 1 = disposed (for Interlocked)

    /// <summary>
    /// Creates a new batch event processor
    /// </summary>
    /// <param name="ringBuffer">The ring buffer to consume from</param>
    /// <param name="sequenceBarrier">Barrier for coordinating with other processors</param>
    /// <param name="eventHandler">Handler for processing events</param>
    public BatchEventProcessor(
        RingBuffer<T> ringBuffer,
        ISequenceBarrier sequenceBarrier,
        IEventHandler<T> eventHandler)
    {
        _ringBuffer = ringBuffer ?? throw new ArgumentNullException(nameof(ringBuffer));
        _sequenceBarrier = sequenceBarrier ?? throw new ArgumentNullException(nameof(sequenceBarrier));
        _eventHandler = eventHandler ?? throw new ArgumentNullException(nameof(eventHandler));
    }

    /// <summary>
    /// Gets the current sequence being processed
    /// </summary>
    public ISequence Sequence { get; } = new Sequence(-1);

    /// <summary>
    /// Gets whether the processor is currently running
    /// </summary>
    public bool IsRunning => Volatile.Read(ref _isRunning) == 1;

    /// <summary>
    /// Start processing events. Thread-safe - only one thread will succeed if called concurrently.
    /// </summary>
    public void Start()
    {
        // Atomic check-and-set to prevent race condition
        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
        {
            return; // Already running
        }

        _processingTask = Task.Run(ProcessEvents, _cts.Token);
    }

    /// <summary>
    /// Stop processing events
    /// </summary>
    public void Stop()
    {
        Volatile.Write(ref _isRunning, 0);
        _cts.Cancel();
        _sequenceBarrier.Alert();
    }

    /// <summary>
    /// Main event processing loop
    /// </summary>
    private void ProcessEvents()
    {
        long nextSequence = Sequence.Value + 1;

        try
        {
            while (!_cts.Token.IsCancellationRequested && Volatile.Read(ref _isRunning) == 1)
            {
                try
                {
                    // Wait for events to be available
                    long availableSequence = _sequenceBarrier.WaitFor(nextSequence);

                    // Process all available events in a batch
                    while (nextSequence <= availableSequence)
                    {
                        T evt = _ringBuffer.Get(nextSequence);
                        bool isEndOfBatch = nextSequence == availableSequence;

                        _eventHandler.OnEvent(evt, nextSequence, isEndOfBatch);

                        nextSequence++;
                    }

                    // Update our sequence to signal we are done processing
                    Sequence.Value = availableSequence;
                }
                catch (AlertException)
                {
                    // Processor is being stopped - exit gracefully
                    break;
                }
                catch (Exception ex)
                {
                    // Handle error and continue processing
                    _eventHandler.OnError(ex);

                    // Update sequence and continue
                    Sequence.Value = nextSequence;
                    nextSequence++;
                }
            }
        }
        finally
        {
            // Notify handler of shutdown
            _eventHandler.OnShutdown();
            Volatile.Write(ref _isRunning, 0);
        }
    }

    /// <summary>
    /// Dispose the processor and wait for it to stop. Idempotent.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
            return; // Already disposed

        Stop();

        // Wait for processing task to complete, handling cancellation gracefully
        try
        {
            _processingTask?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is TaskCanceledException or OperationCanceledException))
        {
            // Expected during shutdown - ignore cancellation exceptions
        }
        catch (TaskCanceledException)
        {
            // Expected during shutdown
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }

        _cts.Dispose();
    }

    /// <summary>
    /// Asynchronously dispose the processor. Idempotent.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
            return; // Already disposed

        Stop();

        if (_processingTask != null)
        {
            try
            {
                await _processingTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // Timeout waiting for task - continue with disposal
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
        }

        _cts.Dispose();
    }
}
