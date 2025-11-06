using System.Reflection;
using HeroMessaging.Abstractions.Idempotency;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Processing.Decorators;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Idempotency.Decorators;

/// <summary>
/// Decorator that adds idempotency support to message processing pipeline.
/// </summary>
/// <remarks>
/// <para>
/// This decorator implements the idempotency pattern by caching processing results
/// and returning cached responses for duplicate requests. This enables exactly-once
/// semantics even in at-least-once delivery scenarios.
/// </para>
/// <para>
/// <strong>Pipeline Order</strong>: This decorator should be positioned early in the pipeline:
/// </para>
/// <list type="number">
/// <item><description>ValidationDecorator - Validate messages first</description></item>
/// <item><description>IdempotencyDecorator - Check cache before expensive operations (this)</description></item>
/// <item><description>RetryDecorator - Avoid retrying cached responses</description></item>
/// <item><description>CircuitBreakerDecorator - Return cached response even if circuit is open</description></item>
/// <item><description>Handler Execution</description></item>
/// </list>
/// <para>
/// <strong>Behavior</strong>:
/// </para>
/// <list type="bullet">
/// <item><description>Cache hit: Returns cached response without invoking inner processor</description></item>
/// <item><description>Cache miss: Executes handler and caches the result per policy configuration</description></item>
/// <item><description>Success: Always cached for <see cref="IIdempotencyPolicy.SuccessTtl"/></description></item>
/// <item><description>Failure: Cached only if <see cref="IIdempotencyPolicy.IsIdempotentFailure"/> returns true</description></item>
/// </list>
/// </remarks>
public sealed class IdempotencyDecorator : MessageProcessorDecorator
{
    private readonly IIdempotencyStore _store;
    private readonly IIdempotencyPolicy _policy;
    private readonly ILogger<IdempotencyDecorator> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotencyDecorator"/> class.
    /// </summary>
    /// <param name="inner">The inner message processor to decorate.</param>
    /// <param name="store">The idempotency store for caching responses.</param>
    /// <param name="policy">The idempotency policy configuration.</param>
    /// <param name="logger">The logger for diagnostic information.</param>
    /// <param name="timeProvider">The time provider for timestamp management.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when any parameter is null.
    /// </exception>
    public IdempotencyDecorator(
        IMessageProcessor inner,
        IIdempotencyStore store,
        IIdempotencyPolicy policy,
        ILogger<IdempotencyDecorator> logger,
        TimeProvider timeProvider)
        : base(inner)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <inheritdoc />
    public override async ValueTask<ProcessingResult> ProcessAsync(
        IMessage message,
        ProcessingContext context,
        CancellationToken cancellationToken = default)
    {
        // Generate idempotency key
        var idempotencyKey = _policy.KeyGenerator.GenerateKey(message, context);

        // Check cache for existing response
        var cachedResponse = await _store.GetAsync(idempotencyKey, cancellationToken);
        if (cachedResponse != null)
        {
            _logger.LogInformation(
                "Idempotent request detected for message {MessageId} with key {IdempotencyKey}, returning cached {Status} response",
                message.MessageId,
                idempotencyKey,
                cachedResponse.Status);

            return cachedResponse.Status == IdempotencyStatus.Success
                ? ProcessingResult.Successful(data: cachedResponse.SuccessResult)
                : ReconstructFailure(cachedResponse);
        }

        // Execute inner processor
        _logger.LogDebug(
            "Processing message {MessageId} with idempotency key {IdempotencyKey}",
            message.MessageId,
            idempotencyKey);

        var result = await _inner.ProcessAsync(message, context, cancellationToken);

        // Store result based on outcome and policy
        if (result.Success)
        {
            await _store.StoreSuccessAsync(
                idempotencyKey,
                result.Data,
                _policy.SuccessTtl,
                cancellationToken);

            _logger.LogDebug(
                "Stored successful result for message {MessageId} with TTL {SuccessTtl}",
                message.MessageId,
                _policy.SuccessTtl);
        }
        else if (_policy.CacheFailures && result.Exception != null && _policy.IsIdempotentFailure(result.Exception))
        {
            await _store.StoreFailureAsync(
                idempotencyKey,
                result.Exception,
                _policy.FailureTtl,
                cancellationToken);

            _logger.LogWarning(
                "Stored idempotent failure for message {MessageId} with TTL {FailureTtl}: {ExceptionType}",
                message.MessageId,
                _policy.FailureTtl,
                result.Exception.GetType().Name);
        }

        return result;
    }

    /// <summary>
    /// Reconstructs an exception from cached failure information.
    /// </summary>
    /// <param name="cachedResponse">The cached failure response.</param>
    /// <returns>A processing result containing the reconstructed exception.</returns>
    private static ProcessingResult ReconstructFailure(IdempotencyResponse cachedResponse)
    {
        Exception exception;

        try
        {
            // Try to reconstruct the original exception type
            var exceptionType = Type.GetType(cachedResponse.FailureType ?? string.Empty);

            if (exceptionType != null && typeof(Exception).IsAssignableFrom(exceptionType))
            {
                // Try to create exception with message constructor
                var constructor = exceptionType.GetConstructor(new[] { typeof(string) });
                if (constructor != null)
                {
                    exception = (Exception)constructor.Invoke(new object?[] { cachedResponse.FailureMessage });
                }
                else
                {
                    // Fall back to parameterless constructor
                    exception = (Exception?)Activator.CreateInstance(exceptionType)
                        ?? new Exception(cachedResponse.FailureMessage);
                }
            }
            else
            {
                // Unknown exception type, use generic Exception
                exception = new Exception(
                    $"[{cachedResponse.FailureType}] {cachedResponse.FailureMessage}");
            }
        }
        catch
        {
            // If reconstruction fails, create a generic exception with the cached message
            exception = new Exception(
                $"[{cachedResponse.FailureType}] {cachedResponse.FailureMessage}");
        }

        return ProcessingResult.Failed(exception, "Idempotent cached failure");
    }
}
