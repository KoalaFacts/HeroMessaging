using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Abstractions.Idempotency;
using HeroMessaging.Configuration;
using HeroMessaging.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroMessaging.Tests.Unit.Processing;

[Trait("Category", "Unit")]
public sealed class CommandProcessorIdempotencyTests
{
    [Fact]
    public async Task SendAsync_WithIdempotency_RunsHandlerOncePerMessageId()
    {
        var counter = new Counter();
        using var provider = BuildProvider(counter, withIdempotency: true);
        await using var processor = new CommandProcessor(provider, NullLogger<CommandProcessor>.Instance);
        var command = new ChargeCard { MessageId = Guid.NewGuid() };

        await processor.SendAsync(command, TestContext.Current.CancellationToken);
        await processor.SendAsync(command, TestContext.Current.CancellationToken);

        Assert.Equal(1, counter.Calls);
    }

    [Fact]
    public async Task SendAsync_WithIdempotency_RunsHandlerForDistinctMessageIds()
    {
        var counter = new Counter();
        using var provider = BuildProvider(counter, withIdempotency: true);
        await using var processor = new CommandProcessor(provider, NullLogger<CommandProcessor>.Instance);

        await processor.SendAsync(new ChargeCard { MessageId = Guid.NewGuid() }, TestContext.Current.CancellationToken);
        await processor.SendAsync(new ChargeCard { MessageId = Guid.NewGuid() }, TestContext.Current.CancellationToken);

        Assert.Equal(2, counter.Calls);
    }

    [Fact]
    public async Task SendAsync_WithoutIdempotency_RunsHandlerForEveryDelivery()
    {
        var counter = new Counter();
        using var provider = BuildProvider(counter, withIdempotency: false);
        await using var processor = new CommandProcessor(provider, NullLogger<CommandProcessor>.Instance);
        var command = new ChargeCard { MessageId = Guid.NewGuid() };

        await processor.SendAsync(command, TestContext.Current.CancellationToken);
        await processor.SendAsync(command, TestContext.Current.CancellationToken);

        Assert.Equal(2, counter.Calls);
    }

    [Fact]
    public async Task SendAsync_WithIdempotency_RethrowsCachedIdempotentFailureWithoutRerunningHandler()
    {
        var counter = new Counter { Throws = () => new ArgumentException("card number is invalid") };
        using var provider = BuildProvider(counter, withIdempotency: true);
        await using var processor = new CommandProcessor(provider, NullLogger<CommandProcessor>.Instance);
        var command = new ChargeCard { MessageId = Guid.NewGuid() };

        var first = await Assert.ThrowsAsync<ArgumentException>(
            () => processor.SendAsync(command, TestContext.Current.CancellationToken));
        var second = await Assert.ThrowsAsync<ArgumentException>(
            () => processor.SendAsync(command, TestContext.Current.CancellationToken));

        Assert.Equal(1, counter.Calls);
        Assert.Contains("card number is invalid", first.Message);
        Assert.Contains("card number is invalid", second.Message);
    }

    [Fact]
    public async Task SendAsync_WithIdempotency_RerunsHandlerAfterTransientFailure()
    {
        var attempts = 0;
        var counter = new Counter
        {
            Throws = () => Interlocked.Increment(ref attempts) == 1 ? new IOException("gateway busy") : null
        };
        using var provider = BuildProvider(counter, withIdempotency: true);
        await using var processor = new CommandProcessor(provider, NullLogger<CommandProcessor>.Instance);
        var command = new ChargeCard { MessageId = Guid.NewGuid() };

        await Assert.ThrowsAsync<IOException>(() => processor.SendAsync(command, TestContext.Current.CancellationToken));
        await processor.SendAsync(command, TestContext.Current.CancellationToken);
        await processor.SendAsync(command, TestContext.Current.CancellationToken);

        Assert.Equal(2, counter.Calls);
    }

    [Fact]
    public async Task SendAsync_WithIdempotency_HandlerCancellationIsNotCached()
    {
        var attempts = 0;
        var counter = new Counter
        {
            Throws = () => Interlocked.Increment(ref attempts) == 1 ? new OperationCanceledException("stopping") : null
        };
        using var provider = BuildProvider(counter, withIdempotency: true);
        await using var processor = new CommandProcessor(provider, NullLogger<CommandProcessor>.Instance);
        var command = new ChargeCard { MessageId = Guid.NewGuid() };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => processor.SendAsync(command, TestContext.Current.CancellationToken));
        await processor.SendAsync(command, TestContext.Current.CancellationToken);

        Assert.Equal(2, counter.Calls);
        Assert.Equal(1, processor.GetMetrics().ProcessedCount);
        Assert.Equal(0, processor.GetMetrics().FailedCount);
    }

    [Fact]
    public async Task SendAsync_WhenIdempotencyStoreFails_ProcessesWithoutDeduplication()
    {
        var counter = new Counter();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(counter);
        services.AddSingleton<ICommandHandler<ChargeCard>, ChargeCardHandler>();
        services.AddSingleton<IIdempotencyStore, FailingStore>();
        services.AddHeroMessaging().WithIdempotency();
        using var provider = services.BuildServiceProvider();
        await using var processor = new CommandProcessor(provider, NullLogger<CommandProcessor>.Instance);
        var command = new ChargeCard { MessageId = Guid.NewGuid() };

        await processor.SendAsync(command, TestContext.Current.CancellationToken);
        await processor.SendAsync(command, TestContext.Current.CancellationToken);

        Assert.Equal(2, counter.Calls);
    }

    [Fact]
    public async Task SendAsync_WithIdempotency_KeyIsScopedToTheCommandHandler()
    {
        var counter = new Counter();
        using var provider = BuildProvider(counter, withIdempotency: true);
        await using var processor = new CommandProcessor(provider, NullLogger<CommandProcessor>.Instance);
        var store = provider.GetRequiredService<IIdempotencyStore>();
        var command = new ChargeCard { MessageId = Guid.NewGuid() };

        await processor.SendAsync(command, TestContext.Current.CancellationToken);

        var key = $"idempotency:{command.MessageId}:handler:{typeof(ChargeCardHandler).FullName}";
        Assert.True(await store.ExistsAsync(key, TestContext.Current.CancellationToken));
    }

    private sealed class FailingStore : IIdempotencyStore
    {
        public ValueTask<IdempotencyResponse?> GetAsync(string idempotencyKey, CancellationToken cancellationToken = default)
            => ValueTask.FromException<IdempotencyResponse?>(new IOException("store offline"));
        public ValueTask StoreSuccessAsync(string idempotencyKey, object? result, TimeSpan ttl, CancellationToken cancellationToken = default)
            => ValueTask.FromException(new IOException("store offline"));
        public ValueTask StoreFailureAsync(string idempotencyKey, Exception exception, TimeSpan ttl, CancellationToken cancellationToken = default)
            => ValueTask.FromException(new IOException("store offline"));
        public ValueTask<bool> ExistsAsync(string idempotencyKey, CancellationToken cancellationToken = default)
            => ValueTask.FromException<bool>(new IOException("store offline"));
        public ValueTask<int> CleanupExpiredAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(0);
    }

    private static ServiceProvider BuildProvider(Counter counter, bool withIdempotency)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(counter);
        services.AddSingleton<ICommandHandler<ChargeCard>, ChargeCardHandler>();
        if (withIdempotency)
            services.AddHeroMessaging().WithIdempotency();
        return services.BuildServiceProvider();
    }

    public sealed record ChargeCard : ICommand
    {
        public Guid MessageId { get; init; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; init; }
        public string? CausationId { get; init; }
        public Dictionary<string, object>? Metadata { get; init; }
    }

    public sealed class Counter
    {
        private int _calls;
        public Func<Exception?>? Throws { get; init; }
        public int Calls => Volatile.Read(ref _calls);
        public void Hit() => Interlocked.Increment(ref _calls);
    }

    public sealed class ChargeCardHandler(Counter counter) : ICommandHandler<ChargeCard>
    {
        public Task HandleAsync(ChargeCard command, CancellationToken cancellationToken = default)
        {
            counter.Hit();
            var failure = counter.Throws?.Invoke();
            return failure is null ? Task.CompletedTask : Task.FromException(failure);
        }
    }
}
