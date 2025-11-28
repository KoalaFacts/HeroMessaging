using HeroMessaging.Abstractions.Policies;

namespace HeroMessaging.Policies;

/// <summary>
/// Internal token bucket state management for rate limiting.
/// Implements the token bucket algorithm with lazy refill for efficient rate limiting.
/// </summary>
internal sealed class TokenBucket : IDisposable
{
    private readonly TokenBucketOptions _options;
    private readonly TimeProvider _timeProvider;

#if NET9_0_OR_GREATER
    private readonly Lock _lock = new();
#else
    private readonly object _lock = new();
#endif

    private double _availableTokens;
    private DateTimeOffset _lastRefillTime;
    private long _totalAcquired;
    private long _totalThrottled;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="TokenBucket"/> class.
    /// </summary>
    /// <param name="options">The token bucket configuration options.</param>
    /// <param name="timeProvider">The time provider for getting current time.</param>
    public TokenBucket(TokenBucketOptions options, TimeProvider timeProvider)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _availableTokens = options.Capacity; // Start full
        _lastRefillTime = _timeProvider.GetUtcNow();
    }

    /// <summary>
    /// Attempts to acquire the specified number of permits from the bucket.
    /// </summary>
    /// <param name="permits">The number of permits to acquire.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A result indicating whether the permits were acquired or the request was throttled.</returns>
    public async ValueTask<RateLimitResult> AcquireAsync(int permits, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_lock)
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(TokenBucket));

                RefillTokens();

                if (_availableTokens >= permits)
                {
                    // Tokens available - acquire
                    _availableTokens -= permits;
                    _totalAcquired++;
                    return RateLimitResult.Success((long)Math.Floor(_availableTokens));
                }

                // Not enough tokens
                _totalThrottled++;

                if (_options.Behavior == RateLimitBehavior.Reject)
                {
                    // Calculate retry after based on refill rate
                    var tokensNeeded = permits - _availableTokens;
                    var retryAfter = TimeSpan.FromSeconds(tokensNeeded / _options.RefillRate);
                    return RateLimitResult.Throttled(retryAfter, "Rate limit exceeded");
                }
            }

            // Queue behavior - wait and retry
            var waitTime = CalculateWaitTime(permits);

            if (waitTime > _options.MaxQueueWait)
            {
                return RateLimitResult.Throttled(waitTime, "Rate limit exceeded - max queue wait time exceeded");
            }

            try
            {
                await Task.Delay(waitTime, _timeProvider, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
        }
    }

    /// <summary>
    /// Gets the current statistics for this token bucket.
    /// </summary>
    /// <returns>Statistics about the bucket's current state and usage.</returns>
    public RateLimiterStatistics GetStatistics()
    {
        lock (_lock)
        {
            RefillTokens();

            return new RateLimiterStatistics
            {
                AvailablePermits = (long)Math.Floor(_availableTokens),
                Capacity = _options.Capacity,
                RefillRate = _options.RefillRate,
                LastRefillTime = _lastRefillTime,
                TotalAcquired = _totalAcquired,
                TotalThrottled = _totalThrottled
            };
        }
    }

    /// <summary>
    /// Disposes the token bucket.
    /// </summary>
    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
        }
    }

    private void RefillTokens()
    {
        // Lazy refill: Calculate tokens based on elapsed time
        var now = _timeProvider.GetUtcNow();
        var elapsed = now - _lastRefillTime;

        if (elapsed > TimeSpan.Zero)
        {
            var tokensToAdd = elapsed.TotalSeconds * _options.RefillRate;
            _availableTokens = Math.Min(_options.Capacity, _availableTokens + tokensToAdd);
            _lastRefillTime = now;
        }
    }

    private TimeSpan CalculateWaitTime(int permits)
    {
        lock (_lock)
        {
            RefillTokens();
            var tokensNeeded = permits - _availableTokens;
            if (tokensNeeded <= 0) return TimeSpan.Zero;

            return TimeSpan.FromSeconds(tokensNeeded / _options.RefillRate);
        }
    }
}
