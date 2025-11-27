using HeroMessaging.RingBuffer.EventHandlers;
using HeroMessaging.RingBuffer.Sequences;

namespace HeroMessaging.RingBuffer.EventProcessors;

/// <summary>
/// Batch event processor for high-throughput scenarios.
/// Processes events in batches to maximize cache efficiency and throughput.
/// </summary>
/// <typeparam name="T">The type of event to process</typeparam>
public sealed class BatchEventProcessor<T> : IEventProcessor where T : class
{
    private readonly RingBuffer<T> _ringBuffer;
    private readonly ISequenceBarrier _sequenceBarrier;
    private readonly IEventHandler<T> _eventHandler;
    private readonly ISequence _sequence = new Sequence(-1);
    private readonly CancellationTokenSource _cts = new();
    private Task? _processingTask;
    private volatile bool _isRunning;

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
    public ISequence Sequence => _sequence;

    /// <summary>
    /// Gets whether the processor is currently running
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// Start processing events
    /// </summary>
    public void Start()
    {
        if (_isRunning)
        {
            return;
        }

        _isRunning = true;
        _processingTask = Task.Run(ProcessEvents, _cts.Token);
    }

    /// <summary>
    /// Stop processing events
    /// </summary>
    public void Stop()
    {
        _isRunning = false;
        _cts.Cancel();
        _sequenceBarrier.Alert();
    }

    /// <summary>
    /// Main event processing loop
    /// </summary>
    private void ProcessEvents()
    {
        long nextSequence = _sequence.Value + 1;

        try
        {
            while (!_cts.Token.IsCancellationRequested && _isRunning)
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

                    // Update our sequence to signal we're done processing
                    _sequence.Value = availableSequence;
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
                    _sequence.Value = nextSequence;
                    nextSequence++;
                }
            }
        }
        finally
        {
            // Notify handler of shutdown
            _eventHandler.OnShutdown();
            _isRunning = false;
        }
    }

    /// <summary>
    /// Dispose the processor and wait for it to stop
    /// </summary>
    public void Dispose()
    {
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
}
