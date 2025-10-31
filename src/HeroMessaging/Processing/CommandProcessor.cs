using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Utilities;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Threading.Tasks.Dataflow;

namespace HeroMessaging.Processing;

/// <summary>
/// Default implementation of <see cref="ICommandProcessor"/> that processes commands through a sequential pipeline with automatic metrics tracking.
/// </summary>
/// <remarks>
/// This implementation provides reliable command processing with the following characteristics:
/// - Sequential processing ensures commands execute in FIFO order
/// - Bounded queue prevents memory exhaustion (capacity: 100 commands)
/// - Automatic metrics collection (processed count, failures, average duration)
/// - Handler resolution via dependency injection
/// - Exception propagation to callers for proper error handling
///
/// Implementation Details:
/// - Uses TPL Dataflow ActionBlock for queue management
/// - Single-threaded processing (MaxDegreeOfParallelism = 1)
/// - Reflection-based handler invocation
/// - Lock-protected metrics for thread safety
/// - Maintains sliding window of last 100 execution durations
///
/// The processor supports both fire-and-forget commands (void) and commands that return results.
/// All processing is asynchronous and respects cancellation tokens.
/// </remarks>
public class CommandProcessor : ICommandProcessor, IProcessor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CommandProcessor> _logger;
    private readonly ActionBlock<Func<Task>> _processingBlock;
    private long _processedCount;
    private long _failedCount;
    private readonly List<long> _durations = new();
    private readonly object _metricsLock = new();

    /// <summary>
    /// Gets a value indicating whether the processor is running and accepting commands.
    /// </summary>
    /// <value>
    /// Always returns <c>true</c> in this implementation as the processor is always ready to accept commands.
    /// </value>
    public bool IsRunning { get; private set; } = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandProcessor"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider used to resolve command handlers.</param>
    /// <param name="logger">Optional logger for diagnostic output. If null, a NullLogger is used.</param>
    /// <remarks>
    /// The processor is configured with:
    /// - MaxDegreeOfParallelism = 1 (sequential processing)
    /// - BoundedCapacity = 100 (prevents unbounded memory growth)
    ///
    /// Command handlers are resolved from the service provider using the pattern:
    /// - ICommandHandler&lt;TCommand&gt; for void commands
    /// - ICommandHandler&lt;TCommand, TResponse&gt; for commands with results
    /// </remarks>
    public CommandProcessor(IServiceProvider serviceProvider, ILogger<CommandProcessor>? logger = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<CommandProcessor>.Instance;

        _processingBlock = new ActionBlock<Func<Task>>(
            async action => await action(),
            new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = 1,
                BoundedCapacity = 100
            });
    }

    public async Task Send(ICommand command, CancellationToken cancellationToken = default)
    {
        CompatibilityHelpers.ThrowIfNull(command, nameof(command));

        var handlerType = typeof(ICommandHandler<>).MakeGenericType(command.GetType());
        var handler = _serviceProvider.GetService(handlerType);

        if (handler == null)
        {
            throw new InvalidOperationException($"No handler found for command type {command.GetType().Name}");
        }

        var tcs = new TaskCompletionSource<bool>();

        var posted = await _processingBlock.SendAsync(async () =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var handleMethod = handlerType.GetMethod("Handle");
                var sw = Stopwatch.StartNew();
                await (Task)handleMethod!.Invoke(handler, [command, cancellationToken])!;
                sw.Stop();

                lock (_metricsLock)
                {
                    _processedCount++;
                    _durations.Add(sw.ElapsedMilliseconds);
                    if (_durations.Count > 100) _durations.RemoveAt(0);
                }

                tcs.SetResult(true);
            }
            catch (OperationCanceledException)
            {
                tcs.SetCanceled(cancellationToken);
            }
            catch (Exception ex)
            {
                lock (_metricsLock)
                {
                    _failedCount++;
                }

                _logger.LogError(ex, "Error processing command {CommandType}", command.GetType().Name);
                tcs.SetException(ex);
            }
        }, cancellationToken);

        if (!posted)
        {
            tcs.SetCanceled(cancellationToken);
        }

        await tcs.Task;
    }

    public async Task<TResponse> Send<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default)
    {
        CompatibilityHelpers.ThrowIfNull(command, nameof(command));

        var handlerType = typeof(ICommandHandler<,>).MakeGenericType(command.GetType(), typeof(TResponse));
        var handler = _serviceProvider.GetService(handlerType);

        if (handler == null)
        {
            throw new InvalidOperationException($"No handler found for command type {command.GetType().Name}");
        }

        var tcs = new TaskCompletionSource<TResponse>();

        var posted = await _processingBlock.SendAsync(async () =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var handleMethod = handlerType.GetMethod("Handle");
                var sw = Stopwatch.StartNew();
                var result = await (Task<TResponse>)handleMethod!.Invoke(handler, [command, cancellationToken])!;
                sw.Stop();

                lock (_metricsLock)
                {
                    _processedCount++;
                    _durations.Add(sw.ElapsedMilliseconds);
                    if (_durations.Count > 100) _durations.RemoveAt(0);
                }

                tcs.SetResult(result);
            }
            catch (OperationCanceledException)
            {
                tcs.SetCanceled(cancellationToken);
            }
            catch (Exception ex)
            {
                lock (_metricsLock)
                {
                    _failedCount++;
                }

                _logger.LogError(ex, "Error processing command {CommandType}", command.GetType().Name);
                tcs.SetException(ex);
            }
        }, cancellationToken);

        if (!posted)
        {
            tcs.SetCanceled(cancellationToken);
        }

        return await tcs.Task;
    }

    /// <summary>
    /// Retrieves current processing metrics for monitoring and diagnostics.
    /// </summary>
    /// <returns>
    /// A <see cref="ProcessorMetrics"/> instance containing current statistics.
    /// </returns>
    /// <remarks>
    /// Metrics are collected automatically during command processing:
    /// - ProcessedCount: Total successful commands (incremented on success)
    /// - FailedCount: Total failed commands (incremented on exception)
    /// - AverageDuration: Rolling average of last 100 command execution times
    ///
    /// Thread-safe: This method uses locking to ensure consistent metric snapshots.
    /// The average duration calculation excludes cancelled operations.
    /// </remarks>
    public IProcessorMetrics GetMetrics()
    {
        lock (_metricsLock)
        {
            return new ProcessorMetrics
            {
                ProcessedCount = _processedCount,
                FailedCount = _failedCount,
                AverageDuration = _durations.Count > 0
                    ? TimeSpan.FromMilliseconds(_durations.Average())
                    : TimeSpan.Zero
            };
        }
    }
}

/// <summary>
/// Implementation of <see cref="IProcessorMetrics"/> providing command processor statistics.
/// </summary>
/// <remarks>
/// This class is an immutable record type that captures a snapshot of processor metrics
/// at a specific point in time. It is returned by <see cref="CommandProcessor.GetMetrics"/>
/// and <see cref="QueryProcessor.GetMetrics"/>.
/// </remarks>
public class ProcessorMetrics : IProcessorMetrics
{
    /// <summary>
    /// Gets the total number of successfully processed commands.
    /// </summary>
    /// <value>
    /// The count of commands that completed without throwing exceptions.
    /// This count increases monotonically and is never reset.
    /// </value>
    public long ProcessedCount { get; init; }

    /// <summary>
    /// Gets the total number of failed command processing attempts.
    /// </summary>
    /// <value>
    /// The count of commands that threw exceptions during processing.
    /// This includes both business logic failures and infrastructure errors.
    /// Cancelled operations are not counted as failures.
    /// </value>
    public long FailedCount { get; init; }

    /// <summary>
    /// Gets the average duration of command processing operations.
    /// </summary>
    /// <value>
    /// The rolling average of the last 100 command execution times.
    /// Returns <see cref="TimeSpan.Zero"/> if no commands have been processed.
    /// </value>
    /// <remarks>
    /// The average is calculated from a sliding window of the most recent 100 executions
    /// to provide a current view of processing performance rather than historical average.
    /// </remarks>
    public TimeSpan AverageDuration { get; init; }
}

/// <summary>
/// Processes commands through the HeroMessaging command pipeline with support for both fire-and-forget and request-response patterns.
/// </summary>
/// <remarks>
/// The command processor implements the Command pattern for executing operations that modify system state.
/// Commands are processed sequentially through a bounded queue to ensure predictable execution order and resource management.
///
/// Design Principles:
/// - Commands represent write operations (create, update, delete)
/// - Sequential processing ensures consistent state modifications
/// - Supports both void and result-returning commands
/// - Bounded capacity prevents memory exhaustion under load
/// - Automatic handler resolution via dependency injection
///
/// Command Types:
/// - ICommand: Fire-and-forget commands that don't return a result
/// - ICommand&lt;TResponse&gt;: Commands that return a typed result
///
/// Processing Characteristics:
/// - Single-threaded execution (MaxDegreeOfParallelism = 1)
/// - Bounded queue capacity (100 commands)
/// - Automatic metrics tracking (processed count, failures, duration)
/// - Exception propagation to caller
///
/// <code>
/// // Fire-and-forget command
/// public class CreateOrderCommand : ICommand
/// {
///     public Guid CustomerId { get; set; }
///     public decimal Amount { get; set; }
/// }
///
/// // Command handler
/// public class CreateOrderHandler : ICommandHandler&lt;CreateOrderCommand&gt;
/// {
///     public async Task Handle(CreateOrderCommand command, CancellationToken cancellationToken)
///     {
///         // Create order logic
///     }
/// }
///
/// // Usage
/// var processor = serviceProvider.GetRequiredService&lt;ICommandProcessor&gt;();
/// await processor.Send(new CreateOrderCommand { CustomerId = id, Amount = 100 });
///
/// // Command with response
/// public class PlaceOrderCommand : ICommand&lt;string&gt;
/// {
///     public Guid CustomerId { get; set; }
/// }
///
/// var orderId = await processor.Send&lt;string&gt;(new PlaceOrderCommand { CustomerId = id });
/// </code>
/// </remarks>
public interface ICommandProcessor
{
    /// <summary>
    /// Sends a fire-and-forget command for processing without expecting a return value.
    /// </summary>
    /// <param name="command">The command to process. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task that completes when the command has been processed successfully.</returns>
    /// <exception cref="ArgumentNullException">Thrown when command is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no handler is registered for the command type.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled via the cancellation token.</exception>
    /// <remarks>
    /// This method:
    /// - Resolves the appropriate ICommandHandler&lt;TCommand&gt; from the service provider
    /// - Queues the command for sequential processing
    /// - Waits for command execution to complete
    /// - Tracks processing metrics (duration, success/failure counts)
    /// - Propagates any exceptions thrown by the handler
    ///
    /// Processing Behavior:
    /// - Commands are processed in FIFO order
    /// - If queue is full (100 items), operation waits until space is available
    /// - Handler exceptions are logged and propagated to caller
    /// - Cancelled operations are tracked but don't increment failure count
    ///
    /// Performance Considerations:
    /// - Async queueing with bounded capacity prevents memory issues
    /// - Sequential processing ensures no concurrency conflicts
    /// - Metrics tracking adds minimal overhead (&lt;1ms)
    ///
    /// <code>
    /// try
    /// {
    ///     await commandProcessor.Send(new UpdateCustomerCommand
    ///     {
    ///         CustomerId = Guid.NewGuid(),
    ///         Name = "John Doe"
    ///     }, cancellationToken);
    ///
    ///     logger.LogInformation("Customer updated successfully");
    /// }
    /// catch (InvalidOperationException ex)
    /// {
    ///     logger.LogError(ex, "No handler registered for UpdateCustomerCommand");
    /// }
    /// </code>
    /// </remarks>
    Task Send(ICommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a command for processing and returns the command result.
    /// </summary>
    /// <typeparam name="TResponse">The type of result returned by the command handler.</typeparam>
    /// <param name="command">The command to process. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task containing the result produced by the command handler.</returns>
    /// <exception cref="ArgumentNullException">Thrown when command is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no handler is registered for the command type.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled via the cancellation token.</exception>
    /// <remarks>
    /// This method:
    /// - Resolves the appropriate ICommandHandler&lt;TCommand, TResponse&gt; from the service provider
    /// - Queues the command for sequential processing
    /// - Waits for command execution and result
    /// - Tracks processing metrics (duration, success/failure counts)
    /// - Propagates any exceptions thrown by the handler
    ///
    /// Processing Behavior:
    /// - Commands are processed in FIFO order
    /// - If queue is full (100 items), operation waits until space is available
    /// - Handler exceptions are logged and propagated to caller
    /// - The returned result is the value produced by the handler
    /// - Cancelled operations throw OperationCanceledException
    ///
    /// Use Cases:
    /// - Commands that return entity IDs (create operations)
    /// - Commands that return confirmation data
    /// - Commands that return calculated results
    /// - Commands requiring acknowledgment with details
    ///
    /// <code>
    /// // Command returning order ID
    /// var orderId = await commandProcessor.Send&lt;Guid&gt;(new CreateOrderCommand
    /// {
    ///     CustomerId = customerId,
    ///     Items = orderItems,
    ///     TotalAmount = 150.00m
    /// });
    ///
    /// logger.LogInformation("Order created with ID: {OrderId}", orderId);
    ///
    /// // Command returning complex result
    /// var result = await commandProcessor.Send&lt;ValidationResult&gt;(new ValidateOrderCommand
    /// {
    ///     OrderId = orderId
    /// });
    ///
    /// if (result.IsValid)
    /// {
    ///     // Process valid order
    /// }
    /// </code>
    /// </remarks>
    Task<TResponse> Send<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default);
}