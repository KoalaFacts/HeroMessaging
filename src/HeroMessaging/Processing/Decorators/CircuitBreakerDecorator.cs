using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace HeroMessaging.Processing.Decorators;

/// <summary>
/// Decorator that implements circuit breaker pattern to prevent cascading failures
/// </summary>
public class CircuitBreakerDecorator : MessageProcessorDecorator
{
    private readonly ILogger<CircuitBreakerDecorator> _logger;
    private readonly CircuitBreakerOptions _options;
    private readonly CircuitBreakerState _state;

    public CircuitBreakerDecorator(
        IMessageProcessor inner,
        ILogger<CircuitBreakerDecorator> logger,
        CircuitBreakerOptions? options = null) : base(inner)
    {
        _logger = logger;
        _options = options ?? new CircuitBreakerOptions();
        _state = new CircuitBreakerState(_options);
    }

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
internal class CircuitBreakerState(CircuitBreakerOptions options)
{
    private readonly CircuitBreakerOptions _options = options;
    private readonly ConcurrentQueue<(DateTime Timestamp, bool Success)> _results = new ConcurrentQueue<(DateTime, bool)>();
    private CircuitState _currentState = CircuitState.Closed;
    private DateTime _lastStateChange = DateTime.UtcNow;
    private int _halfOpenSuccesses;
    private readonly object _stateLock = new();

    public CircuitState CurrentState => _currentState;
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
                    if (DateTime.UtcNow - _lastStateChange >= _options.BreakDuration)
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
        var now = DateTime.UtcNow;
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
        var now = DateTime.UtcNow;
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
            _lastStateChange = DateTime.UtcNow;
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
        var cutoff = DateTime.UtcNow - _options.SamplingDuration;
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
public class CircuitBreakerOpenException(string message) : Exception(message)
{
}