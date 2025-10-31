using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace HeroMessaging.Processing.Decorators;

/// <summary>
/// Decorator that implements circuit breaker pattern to prevent cascading failures
/// </summary>
/// <remarks>
/// The circuit breaker pattern protects the system from cascading failures by temporarily
/// blocking calls to a failing downstream service, allowing it time to recover. The circuit
/// operates in three states:
///
/// - Closed: Normal operation, requests flow through
/// - Open: Failures detected, requests are rejected immediately
/// - HalfOpen: Testing recovery, limited requests allowed
///
/// State transitions occur based on:
/// - Failure threshold: Number of failures before opening
/// - Failure rate: Percentage of failures in the sampling window
/// - Break duration: Time to wait before attempting recovery
/// - Success threshold: Consecutive successes needed to close from half-open
///
/// The circuit monitors failures within a sliding time window and opens when either
/// the failure count or failure rate exceeds configured thresholds. After the break
/// duration, it transitions to half-open to test recovery with a single request.
/// Three consecutive successes close the circuit, while any failure reopens it.
///
/// Example usage:
/// <code>
/// var options = new CircuitBreakerOptions
/// {
///     FailureThreshold = 5,
///     FailureRateThreshold = 0.5,
///     BreakDuration = TimeSpan.FromSeconds(30)
/// };
/// var decorator = new CircuitBreakerDecorator(processor, logger, TimeProvider.System, options);
/// </code>
/// </remarks>
public class CircuitBreakerDecorator : MessageProcessorDecorator
{
    private readonly ILogger<CircuitBreakerDecorator> _logger;
    private readonly CircuitBreakerOptions _options;
    private readonly CircuitBreakerState _state;

    /// <summary>
    /// Initializes a new instance of the <see cref="CircuitBreakerDecorator"/> class.
    /// </summary>
    /// <param name="inner">The inner message processor to decorate</param>
    /// <param name="logger">Logger for circuit breaker state changes and diagnostics</param>
    /// <param name="timeProvider">Time provider for testable time-based operations</param>
    /// <param name="options">Circuit breaker configuration options, or null to use defaults</param>
    /// <exception cref="ArgumentNullException">Thrown when timeProvider is null</exception>
    public CircuitBreakerDecorator(
        IMessageProcessor inner,
        ILogger<CircuitBreakerDecorator> logger,
        TimeProvider timeProvider,
        CircuitBreakerOptions? options = null) : base(inner)
    {
        _logger = logger;
        _options = options ?? new CircuitBreakerOptions();
        _state = new CircuitBreakerState(_options, timeProvider ?? throw new ArgumentNullException(nameof(timeProvider)));
    }

    /// <summary>
    /// Processes a message through the circuit breaker, enforcing failure protection.
    /// </summary>
    /// <param name="message">The message to process</param>
    /// <param name="context">The processing context containing execution metadata</param>
    /// <param name="cancellationToken">Cancellation token to abort processing</param>
    /// <returns>A task containing the processing result, or a failed result if the circuit is open</returns>
    /// <remarks>
    /// This method checks the circuit state before allowing processing. If the circuit is open,
    /// it immediately returns a failed result with a CircuitBreakerOpenException. If closed or
    /// half-open, it processes the message and records the outcome to update circuit state.
    ///
    /// Successful processing decrements the failure count and may close an open circuit.
    /// Failed processing increments the failure count and may open the circuit if thresholds
    /// are exceeded. All state changes are logged for diagnostics.
    ///
    /// The circuit breaker does not catch exceptions - they are recorded as failures and rethrown
    /// to preserve the original error handling behavior.
    /// </remarks>
    public override async ValueTask<ProcessingResult> ProcessAsync(
        IMessage message,
        ProcessingContext context,
        CancellationToken cancellationToken = default)
    {
        if (!_state.CanProcess())
        {
            _logger.LogWarning(
                "Circuit breaker is {State}. Rejecting message {MessageId}",
                _state.CurrentState,
                message.MessageId);

            return ProcessingResult.Failed(
                new CircuitBreakerOpenException($"Circuit breaker is {_state.CurrentState}"),
                $"Circuit breaker is {_state.CurrentState}");
        }

        try
        {
            var result = await _inner.ProcessAsync(message, context, cancellationToken);

            if (result.Success)
            {
                _state.RecordSuccess();

                if (_state.StateChanged)
                {
                    _logger.LogInformation(
                        "Circuit breaker state changed to {State} after success",
                        _state.CurrentState);
                }
            }
            else
            {
                _state.RecordFailure();

                if (_state.StateChanged)
                {
                    _logger.LogWarning(
                        "Circuit breaker state changed to {State} after failure. Failure rate: {Rate:P}",
                        _state.CurrentState,
                        _state.FailureRate);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _state.RecordFailure();

            if (_state.StateChanged)
            {
                _logger.LogError(ex,
                    "Circuit breaker state changed to {State} after exception. Failure rate: {Rate:P}",
                    _state.CurrentState,
                    _state.FailureRate);
            }

            throw;
        }
    }
}

/// <summary>
/// Circuit breaker configuration options
/// </summary>
public class CircuitBreakerOptions
{
    /// <summary>
    /// Number of failures before opening the circuit
    /// </summary>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>
    /// Time window for counting failures
    /// </summary>
    public TimeSpan SamplingDuration { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Minimum throughput required before calculating failure rate
    /// </summary>
    public int MinimumThroughput { get; set; } = 10;

    /// <summary>
    /// Duration to wait before attempting to half-open the circuit
    /// </summary>
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Failure rate threshold (0.0 to 1.0)
    /// </summary>
    public double FailureRateThreshold { get; set; } = 0.5;
}

/// <summary>
/// Manages circuit breaker state transitions
/// </summary>
internal class CircuitBreakerState(CircuitBreakerOptions options, TimeProvider timeProvider)
{
    private readonly CircuitBreakerOptions _options = options;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ConcurrentQueue<(DateTime Timestamp, bool Success)> _results = new ConcurrentQueue<(DateTime, bool)>();
    private CircuitState _currentState = CircuitState.Closed;
    private DateTime _lastStateChange = timeProvider.GetUtcNow().DateTime;
    private int _halfOpenSuccesses;
    private readonly object _stateLock = new();

    public CircuitState CurrentState => _currentState;

    /// <summary>
    /// Gets a value indicating whether the circuit breaker state changed during the last operation
    /// </summary>
    public bool StateChanged { get; private set; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool CanProcess()
    {
        lock (_stateLock)
        {
            StateChanged = false;

            switch (_currentState)
            {
                case CircuitState.Closed:
                    return true;

                case CircuitState.Open:
                    if (_timeProvider.GetUtcNow().DateTime - _lastStateChange >= _options.BreakDuration)
                    {
                        TransitionTo(CircuitState.HalfOpen);
                        return true;
                    }
                    return false;

                case CircuitState.HalfOpen:
                    return true;

                default:
                    return false;
            }
        }
    }

    public void RecordSuccess()
    {
        var now = _timeProvider.GetUtcNow().DateTime;
        _results.Enqueue((now, true));
        CleanOldResults(now);

        lock (_stateLock)
        {
            StateChanged = false;

            if (_currentState == CircuitState.HalfOpen)
            {
                _halfOpenSuccesses++;
                if (_halfOpenSuccesses >= 3) // Require 3 consecutive successes to close
                {
                    TransitionTo(CircuitState.Closed);
                }
            }
        }
    }

    public void RecordFailure()
    {
        var now = _timeProvider.GetUtcNow().DateTime;
        _results.Enqueue((now, false));
        CleanOldResults(now);

        lock (_stateLock)
        {
            StateChanged = false;

            switch (_currentState)
            {
                case CircuitState.Closed:
                    if (ShouldOpen())
                    {
                        TransitionTo(CircuitState.Open);
                    }
                    break;

                case CircuitState.HalfOpen:
                    TransitionTo(CircuitState.Open);
                    break;
            }
        }
    }

    public double FailureRate
    {
        get
        {
            var validResults = GetValidResults();
            if (!validResults.Any()) return 0;

            var failures = validResults.Count(r => !r.Success);
            return (double)failures / validResults.Count;
        }
    }

    private void TransitionTo(CircuitState newState)
    {
        if (_currentState != newState)
        {
            _currentState = newState;
            _lastStateChange = _timeProvider.GetUtcNow().DateTime;
            StateChanged = true;

            if (newState == CircuitState.HalfOpen)
            {
                _halfOpenSuccesses = 0;
            }
        }
    }

    private bool ShouldOpen()
    {
        var validResults = GetValidResults();

        if (validResults.Count < _options.MinimumThroughput)
            return false;

        var failures = validResults.Count(r => !r.Success);
        var failureRate = (double)failures / validResults.Count;

        return failures >= _options.FailureThreshold ||
               failureRate >= _options.FailureRateThreshold;
    }

    private List<(DateTime Timestamp, bool Success)> GetValidResults()
    {
        var cutoff = _timeProvider.GetUtcNow().DateTime - _options.SamplingDuration;
        return _results.Where(r => r.Timestamp >= cutoff).ToList();
    }

    private void CleanOldResults(DateTime now)
    {
        var cutoff = now - _options.SamplingDuration;
        while (_results.TryPeek(out var result) && result.Timestamp < cutoff)
        {
            _results.TryDequeue(out _);
        }
    }
}

/// <summary>
/// Circuit breaker states
/// </summary>
public enum CircuitState
{
    /// <summary>
    /// Circuit is closed, requests flow normally
    /// </summary>
    Closed,

    /// <summary>
    /// Circuit is open, requests are rejected
    /// </summary>
    Open,

    /// <summary>
    /// Circuit is half-open, limited requests allowed to test recovery
    /// </summary>
    HalfOpen
}

/// <summary>
/// Exception thrown when circuit breaker is open
/// </summary>
/// <remarks>
/// This exception is thrown when a request is rejected because the circuit breaker is in the Open state.
/// It indicates that the circuit has detected too many failures and is temporarily blocking requests
/// to allow the downstream service time to recover. Clients should handle this exception by either
/// failing fast or using an alternative processing path.
/// </remarks>
public class CircuitBreakerOpenException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CircuitBreakerOpenException"/> class with a default message.
    /// </summary>
    public CircuitBreakerOpenException() : base("Circuit breaker is open") { }

    /// <summary>
    /// Initializes a new instance of the <see cref="CircuitBreakerOpenException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error</param>
    public CircuitBreakerOpenException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="CircuitBreakerOpenException"/> class with a specified error message and inner exception.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception</param>
    /// <param name="innerException">The exception that is the cause of the current exception</param>
    public CircuitBreakerOpenException(string message, Exception innerException) : base(message, innerException) { }
}