using System.Collections.Concurrent;
using System.Threading;
using HeroMessaging.Abstractions.Policies;

namespace HeroMessaging.Policies;

/// <summary>
/// Token Bucket rate limiter implementation.
/// Allows controlled bursts while maintaining a steady-state rate.
/// </summary>
/// <remarks>
/// The Token Bucket algorithm works by maintaining a bucket of tokens.
/// Tokens are added at a fixed rate (RefillRate), and each request consumes one or more tokens.
/// When the bucket is empty, requests are either queued or rejected based on configuration.
/// Thread-safe for concurrent access.
/// </remarks>
public sealed class TokenBucketRateLimiter : IRateLimiter, IDisposable
{
    private readonly TokenBucketOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly TokenBucket _globalBucket;
    private readonly ConcurrentDictionary<string, TokenBucket>? _scopedBuckets;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="TokenBucketRateLimiter"/> class.
    /// </summary>
    /// <param name="options">Configuration options for the rate limiter.</param>
    /// <param name="timeProvider">Time provider for testability. If null, uses <see cref="TimeProvider.System"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when options contain invalid values.</exception>
    public TokenBucketRateLimiter(TokenBucketOptions options, TimeProvider? timeProvider = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();

        _timeProvider = timeProvider ?? TimeProvider.System;
        _globalBucket = new TokenBucket(_options, _timeProvider);

        if (_options.EnableScoping)
        {
            _scopedBuckets = new ConcurrentDictionary<string, TokenBucket>();
        }
    }

    /// <inheritdoc />
    public async ValueTask<RateLimitResult> AcquireAsync(
        string? key = null,
        int permits = 1,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TokenBucketRateLimiter));

        if (permits <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(permits), permits, "Permits must be positive.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var bucket = GetBucket(key);
        return await bucket.AcquireAsync(permits, cancellationToken);
    }

    /// <inheritdoc />
    public RateLimiterStatistics GetStatistics(string? key = null)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TokenBucketRateLimiter));

        var bucket = key != null && _scopedBuckets != null && _scopedBuckets.TryGetValue(key, out var scopedBucket)
            ? scopedBucket
            : _globalBucket;

        return bucket.GetStatistics();
    }

    /// <summary>
    /// Releases resources used by the rate limiter.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _globalBucket?.Dispose();

        if (_scopedBuckets != null)
        {
            foreach (var bucket in _scopedBuckets.Values)
            {
                bucket.Dispose();
            }
            _scopedBuckets.Clear();
        }
    }

    private TokenBucket GetBucket(string? key)
    {
        if (key == null || !_options.EnableScoping || _scopedBuckets == null)
        {
            return _globalBucket;
        }

        return _scopedBuckets.GetOrAdd(key, _ => new TokenBucket(_options, _timeProvider));
    }
}
