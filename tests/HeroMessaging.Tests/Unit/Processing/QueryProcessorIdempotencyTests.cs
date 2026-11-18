using System.Text.Json;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Abstractions.Idempotency;
using HeroMessaging.Abstractions.Queries;
using HeroMessaging.Configuration;
using HeroMessaging.Idempotency.Storage;
using HeroMessaging.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroMessaging.Tests.Unit.Processing;

[Trait("Category", "Unit")]
public sealed class QueryProcessorIdempotencyTests
{
    [Fact]
    public async Task SendAsync_WithIdempotency_RunsHandlerOnceAndReturnsTheCachedResponse()
    {
        var counter = new Counter();
        using var provider = BuildProvider(counter, withIdempotency: true);
        await using var processor = new QueryProcessor(provider, NullLogger<QueryProcessor>.Instance);
        var query = new GetBalance { Account = "acc-1" };

        var first = await processor.SendAsync(query, TestContext.Current.CancellationToken);
        var second = await processor.SendAsync(query, TestContext.Current.CancellationToken);

        Assert.Equal(1, counter.Calls);
        Assert.Equal("balance-1", first.Snapshot);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task SendAsync_WithIdempotency_RunsHandlerForDistinctMessageIds()
    {
        var counter = new Counter();
        using var provider = BuildProvider(counter, withIdempotency: true);
        await using var processor = new QueryProcessor(provider, NullLogger<QueryProcessor>.Instance);

        var first = await processor.SendAsync(new GetBalance { Account = "a" }, TestContext.Current.CancellationToken);
        var second = await processor.SendAsync(new GetBalance { Account = "a" }, TestContext.Current.CancellationToken);

        Assert.Equal(2, counter.Calls);
        Assert.Equal("balance-2", second.Snapshot);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task SendAsync_WithoutIdempotency_RunsHandlerForEveryDelivery()
    {
        var counter = new Counter();
        using var provider = BuildProvider(counter, withIdempotency: false);
        await using var processor = new QueryProcessor(provider, NullLogger<QueryProcessor>.Instance);
        var query = new GetBalance { Account = "a" };

        await processor.SendAsync(query, TestContext.Current.CancellationToken);
        await processor.SendAsync(query, TestContext.Current.CancellationToken);

        Assert.Equal(2, counter.Calls);
    }

    [Fact]
    public async Task SendAsync_WithIdempotency_RethrowsCachedIdempotentFailureWithoutRerunningHandler()
    {
        var counter = new Counter { Throws = () => new KeyNotFoundException("no such account") };
        using var provider = BuildProvider(counter, withIdempotency: true);
        await using var processor = new QueryProcessor(provider, NullLogger<QueryProcessor>.Instance);
        var query = new GetBalance { Account = "missing" };

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => processor.SendAsync(query, TestContext.Current.CancellationToken));
        var second = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => processor.SendAsync(query, TestContext.Current.CancellationToken));

        Assert.Equal(1, counter.Calls);
        Assert.Contains("no such account", second.Message);
    }

    [Fact]
    public async Task SendAsync_WithIdempotency_RerunsHandlerAfterTransientFailure()
    {
        var attempts = 0;
        var counter = new Counter { Throws = () => ++attempts == 1 ? new TimeoutException("read timeout") : null };
        using var provider = BuildProvider(counter, withIdempotency: true);
        await using var processor = new QueryProcessor(provider, NullLogger<QueryProcessor>.Instance);
        var query = new GetBalance { Account = "a" };

        await Assert.ThrowsAsync<TimeoutException>(
            () => processor.SendAsync(query, TestContext.Current.CancellationToken));
        var retried = await processor.SendAsync(query, TestContext.Current.CancellationToken);
        var redelivered = await processor.SendAsync(query, TestContext.Current.CancellationToken);

        Assert.Equal(2, counter.Calls);
        Assert.Equal(retried, redelivered);
    }

    [Fact]
    public async Task SendAsync_WithIdempotency_CancelledQueryIsNotCached()
    {
        var attempts = 0;
        var counter = new Counter { Throws = () => ++attempts == 1 ? new OperationCanceledException("cancelled") : null };
        using var provider = BuildProvider(counter, withIdempotency: true);
        await using var processor = new QueryProcessor(provider, NullLogger<QueryProcessor>.Instance);
        var query = new GetBalance { Account = "a" };

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => processor.SendAsync(query, TestContext.Current.CancellationToken));
        var redelivered = await processor.SendAsync(query, TestContext.Current.CancellationToken);

        Assert.Equal(2, counter.Calls);
        Assert.Equal("balance-2", redelivered.Snapshot);
    }

    [Fact]
    public async Task SendAsync_StoreReturnsJson_ConvertsCachedResponseToResponseType()
    {
        var counter = new Counter();
        using var provider = BuildProvider(counter, withIdempotency: true,
            rewrite: r => JsonSerializer.SerializeToElement(r));
        await using var first = new QueryProcessor(provider, NullLogger<QueryProcessor>.Instance);
        await using var second = new QueryProcessor(provider, NullLogger<QueryProcessor>.Instance);
        var query = new GetBalance { Account = "a" };

        var original = await first.SendAsync(query, TestContext.Current.CancellationToken);
        var redelivered = await second.SendAsync(query, TestContext.Current.CancellationToken);

        Assert.Equal(1, counter.Calls);
        Assert.Equal(original, redelivered);
    }

    [Fact]
    public async Task SendAsync_StoreReturnsUnexpectedType_ThrowsActionableError()
    {
        var counter = new Counter();
        using var provider = BuildProvider(counter, withIdempotency: true, rewrite: _ => 17);
        await using var processor = new QueryProcessor(provider, NullLogger<QueryProcessor>.Instance);
        var query = new GetBalance { Account = "a" };
        await processor.SendAsync(query, TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => processor.SendAsync(query, TestContext.Current.CancellationToken));

        Assert.Equal(1, counter.Calls);
        Assert.Contains("[HM-IDEMPOTENCY-RESPONSE]", error.Message);
        Assert.Contains("query GetBalance", error.Message);
    }

    [Fact]
    public async Task SendAsync_WithIdempotency_KeyIsScopedToTheQueryType()
    {
        var counter = new Counter();
        using var provider = BuildProvider(counter, withIdempotency: true);
        await using var processor = new QueryProcessor(provider, NullLogger<QueryProcessor>.Instance);
        var store = provider.GetRequiredService<IIdempotencyStore>();
        var query = new GetBalance { Account = "a" };

        await processor.SendAsync(query, TestContext.Current.CancellationToken);

        Assert.True(await store.ExistsAsync(
            $"idempotency:{query.MessageId}:handler:{typeof(GetBalance).FullName}",
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SendAsync_DifferentQueriesSharingAMessageIdAndResponseType_RunEachHandler()
    {
        var counter = new Counter();
        using var provider = BuildProvider(counter, withIdempotency: true);
        await using var processor = new QueryProcessor(provider, NullLogger<QueryProcessor>.Instance);
        var id = Guid.NewGuid();

        var balance = await processor.SendAsync(new GetBalance { MessageId = id, Account = "a" },
            TestContext.Current.CancellationToken);
        var limit = await processor.SendAsync(new GetLimit { MessageId = id, Account = "a" },
            TestContext.Current.CancellationToken);

        Assert.Equal(2, counter.Calls);
        Assert.StartsWith("balance-", balance.Snapshot);
        Assert.StartsWith("limit-", limit.Snapshot);
    }

    private static ServiceProvider BuildProvider(Counter counter, bool withIdempotency, Func<object?, object?>? rewrite = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(counter);
        services.AddSingleton<IQueryHandler<GetBalance, AccountSnapshot>, GetBalanceHandler>();
        services.AddSingleton<IQueryHandler<GetLimit, AccountSnapshot>, GetLimitHandler>();
        if (rewrite is not null)
            services.AddSingleton<IIdempotencyStore>(new RewritingStore(rewrite));
        if (withIdempotency)
            services.AddHeroMessaging().WithIdempotency();
        return services.BuildServiceProvider();
    }

    /// <summary>Rewrites each success result before storing it, like a durable store that reads it back as JSON.</summary>
    private sealed class RewritingStore(Func<object?, object?> rewrite) : IIdempotencyStore
    {
        private readonly InMemoryIdempotencyStore _inner = new(TimeProvider.System);

        public ValueTask<IdempotencyResponse?> GetAsync(string idempotencyKey, CancellationToken cancellationToken = default)
            => _inner.GetAsync(idempotencyKey, cancellationToken);
        public ValueTask StoreSuccessAsync(string idempotencyKey, object? result, TimeSpan ttl, CancellationToken cancellationToken = default)
            => _inner.StoreSuccessAsync(idempotencyKey, rewrite(result), ttl, cancellationToken);
        public ValueTask StoreFailureAsync(string idempotencyKey, Exception exception, TimeSpan ttl, CancellationToken cancellationToken = default)
            => _inner.StoreFailureAsync(idempotencyKey, exception, ttl, cancellationToken);
        public ValueTask<bool> ExistsAsync(string idempotencyKey, CancellationToken cancellationToken = default)
            => _inner.ExistsAsync(idempotencyKey, cancellationToken);
        public ValueTask<int> CleanupExpiredAsync(CancellationToken cancellationToken = default)
            => _inner.CleanupExpiredAsync(cancellationToken);
    }

    public sealed record GetBalance : IQuery<AccountSnapshot>
    {
        public Guid MessageId { get; init; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; init; }
        public string? CausationId { get; init; }
        public Dictionary<string, object>? Metadata { get; init; }
        public string Account { get; init; } = "";
    }

    public sealed record GetLimit : IQuery<AccountSnapshot>
    {
        public Guid MessageId { get; init; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; init; }
        public string? CausationId { get; init; }
        public Dictionary<string, object>? Metadata { get; init; }
        public string Account { get; init; } = "";
    }

    public sealed record AccountSnapshot(string Snapshot, string Account);

    public sealed class Counter
    {
        private int _calls;
        public Func<Exception?>? Throws { get; init; }
        public int Calls => Volatile.Read(ref _calls);
        public int Hit() => Interlocked.Increment(ref _calls);
    }

    public sealed class GetBalanceHandler(Counter counter) : IQueryHandler<GetBalance, AccountSnapshot>
    {
        public Task<AccountSnapshot> HandleAsync(GetBalance query, CancellationToken cancellationToken = default)
        {
            var calls = counter.Hit();
            if (counter.Throws?.Invoke() is { } ex)
                throw ex;
            return Task.FromResult(new AccountSnapshot($"balance-{calls}", query.Account));
        }
    }

    public sealed class GetLimitHandler(Counter counter) : IQueryHandler<GetLimit, AccountSnapshot>
    {
        public Task<AccountSnapshot> HandleAsync(GetLimit query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AccountSnapshot($"limit-{counter.Hit()}", query.Account));
    }
}
