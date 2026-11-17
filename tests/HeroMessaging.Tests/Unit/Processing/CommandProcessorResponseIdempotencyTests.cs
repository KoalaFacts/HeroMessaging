using System.Text.Json;
using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Abstractions.Idempotency;
using HeroMessaging.Configuration;
using HeroMessaging.Idempotency.Storage;
using HeroMessaging.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroMessaging.Tests.Unit.Processing;

[Trait("Category", "Unit")]
public sealed class CommandProcessorResponseIdempotencyTests
{
    [Fact]
    public async Task SendAsyncWithResponse_WithIdempotency_RunsHandlerOnceAndReturnsTheCachedResponse()
    {
        var counter = new Counter();
        using var provider = BuildProvider(counter, withIdempotency: true);
        await using var processor = new CommandProcessor(provider, NullLogger<CommandProcessor>.Instance);
        var command = new AuthorizePayment { MessageId = Guid.NewGuid(), Amount = 42m };

        var first = await processor.SendAsync(command, TestContext.Current.CancellationToken);
        var second = await processor.SendAsync(command, TestContext.Current.CancellationToken);

        Assert.Equal(1, counter.Calls);
        Assert.Equal("auth-1", first.AuthorizationCode);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task SendAsyncWithResponse_WithIdempotency_RunsHandlerForDistinctMessageIds()
    {
        var counter = new Counter();
        using var provider = BuildProvider(counter, withIdempotency: true);
        await using var processor = new CommandProcessor(provider, NullLogger<CommandProcessor>.Instance);

        var first = await processor.SendAsync(new AuthorizePayment { Amount = 1m }, TestContext.Current.CancellationToken);
        var second = await processor.SendAsync(new AuthorizePayment { Amount = 2m }, TestContext.Current.CancellationToken);

        Assert.Equal(2, counter.Calls);
        Assert.Equal("auth-1", first.AuthorizationCode);
        Assert.Equal("auth-2", second.AuthorizationCode);
    }

    [Fact]
    public async Task SendAsyncWithResponse_WithoutIdempotency_RunsHandlerForEveryDelivery()
    {
        var counter = new Counter();
        using var provider = BuildProvider(counter, withIdempotency: false);
        await using var processor = new CommandProcessor(provider, NullLogger<CommandProcessor>.Instance);
        var command = new AuthorizePayment { Amount = 42m };

        await processor.SendAsync(command, TestContext.Current.CancellationToken);
        var second = await processor.SendAsync(command, TestContext.Current.CancellationToken);

        Assert.Equal(2, counter.Calls);
        Assert.Equal("auth-2", second.AuthorizationCode);
    }

    [Fact]
    public async Task SendAsyncWithResponse_WithIdempotency_RethrowsCachedIdempotentFailureWithoutRerunningHandler()
    {
        var counter = new Counter { Throws = () => new ArgumentException("amount must be positive") };
        using var provider = BuildProvider(counter, withIdempotency: true);
        await using var processor = new CommandProcessor(provider, NullLogger<CommandProcessor>.Instance);
        var command = new AuthorizePayment { Amount = -1m };

        await Assert.ThrowsAsync<ArgumentException>(
            () => processor.SendAsync(command, TestContext.Current.CancellationToken));
        var second = await Assert.ThrowsAsync<ArgumentException>(
            () => processor.SendAsync(command, TestContext.Current.CancellationToken));

        Assert.Equal(1, counter.Calls);
        Assert.Contains("amount must be positive", second.Message);
    }

    [Fact]
    public async Task SendAsyncWithResponse_WithIdempotency_RerunsHandlerAfterTransientFailure()
    {
        var attempts = 0;
        var counter = new Counter { Throws = () => ++attempts == 1 ? new TimeoutException("gateway timeout") : null };
        using var provider = BuildProvider(counter, withIdempotency: true);
        await using var processor = new CommandProcessor(provider, NullLogger<CommandProcessor>.Instance);
        var command = new AuthorizePayment { Amount = 5m };

        await Assert.ThrowsAsync<TimeoutException>(
            () => processor.SendAsync(command, TestContext.Current.CancellationToken));
        var retried = await processor.SendAsync(command, TestContext.Current.CancellationToken);
        var redelivered = await processor.SendAsync(command, TestContext.Current.CancellationToken);

        Assert.Equal(2, counter.Calls);
        Assert.Equal("auth-2", retried.AuthorizationCode);
        Assert.Equal(retried, redelivered);
    }

    [Fact]
    public async Task SendAsyncWithResponse_StoreReturnsJson_ConvertsCachedResponseToResponseType()
    {
        var counter = new Counter();
        using var provider = BuildProvider(counter, withIdempotency: true, jsonStore: true);
        await using var first = new CommandProcessor(provider, NullLogger<CommandProcessor>.Instance);
        await using var second = new CommandProcessor(provider, NullLogger<CommandProcessor>.Instance);
        var command = new AuthorizePayment { Amount = 42m };

        var original = await first.SendAsync(command, TestContext.Current.CancellationToken);
        var redelivered = await second.SendAsync(command, TestContext.Current.CancellationToken);

        Assert.Equal(1, counter.Calls);
        Assert.Equal(original, redelivered);
    }

    [Fact]
    public async Task SendAsyncWithResponse_StoreReturnsUnexpectedType_ThrowsActionableError()
    {
        var counter = new Counter();
        using var provider = BuildProvider(counter, withIdempotency: true, wrongTypeStore: true);
        await using var processor = new CommandProcessor(provider, NullLogger<CommandProcessor>.Instance);
        var command = new AuthorizePayment { Amount = 42m };
        await processor.SendAsync(command, TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => processor.SendAsync(command, TestContext.Current.CancellationToken));

        Assert.Equal(1, counter.Calls);
        Assert.Contains("[HM-IDEMPOTENCY-RESPONSE]", error.Message);
        Assert.Contains(nameof(PaymentAuthorization), error.Message);
    }

    private static ServiceProvider BuildProvider(Counter counter, bool withIdempotency, bool jsonStore = false,
        bool wrongTypeStore = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(counter);
        services.AddSingleton<ICommandHandler<AuthorizePayment, PaymentAuthorization>, AuthorizePaymentHandler>();
        if (jsonStore)
            services.AddSingleton<IIdempotencyStore>(new RewritingStore(r => JsonSerializer.SerializeToElement(r)));
        if (wrongTypeStore)
            services.AddSingleton<IIdempotencyStore>(new RewritingStore(_ => 17));
        if (withIdempotency)
            services.AddHeroMessaging().WithIdempotency();
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// An in-memory store that rewrites each success result before storing it, like a durable store that reads
    /// the response back as JSON.
    /// </summary>
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

    public sealed record AuthorizePayment : ICommand<PaymentAuthorization>
    {
        public Guid MessageId { get; init; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; init; }
        public string? CausationId { get; init; }
        public Dictionary<string, object>? Metadata { get; init; }
        public decimal Amount { get; init; }
    }

    public sealed record PaymentAuthorization(string AuthorizationCode, decimal Amount);

    public sealed class Counter
    {
        private int _calls;
        public Func<Exception?>? Throws { get; init; }
        public int Calls => Volatile.Read(ref _calls);
        public int Hit() => Interlocked.Increment(ref _calls);
    }

    public sealed class AuthorizePaymentHandler(Counter counter) : ICommandHandler<AuthorizePayment, PaymentAuthorization>
    {
        public Task<PaymentAuthorization> HandleAsync(AuthorizePayment command, CancellationToken cancellationToken = default)
        {
            var call = counter.Hit();
            var failure = counter.Throws?.Invoke();
            return failure is null
                ? Task.FromResult(new PaymentAuthorization($"auth-{call}", command.Amount))
                : Task.FromException<PaymentAuthorization>(failure);
        }
    }
}
