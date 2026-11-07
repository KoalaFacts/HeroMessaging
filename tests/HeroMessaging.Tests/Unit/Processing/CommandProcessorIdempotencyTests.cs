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
        var command = new DuplicateCommand { MessageId = Guid.NewGuid() };

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

        await processor.SendAsync(new DuplicateCommand { MessageId = Guid.NewGuid() }, TestContext.Current.CancellationToken);
        await processor.SendAsync(new DuplicateCommand { MessageId = Guid.NewGuid() }, TestContext.Current.CancellationToken);

        Assert.Equal(2, counter.Calls);
    }

    [Fact]
    public async Task SendAsync_WithoutIdempotency_RunsHandlerForEveryDelivery()
    {
        var counter = new Counter();
        using var provider = BuildProvider(counter, withIdempotency: false);
        await using var processor = new CommandProcessor(provider, NullLogger<CommandProcessor>.Instance);
        var command = new DuplicateCommand { MessageId = Guid.NewGuid() };

        await processor.SendAsync(command, TestContext.Current.CancellationToken);
        await processor.SendAsync(command, TestContext.Current.CancellationToken);

        Assert.Equal(2, counter.Calls);
    }

    [Fact]
    public async Task SendAsync_WithIdempotency_RethrowsCachedIdempotentFailureWithoutRerunningHandler()
    {
        var counter = new Counter { Throws = () => new ArgumentException("invalid order") };
        using var provider = BuildProvider(counter, withIdempotency: true);
        await using var processor = new CommandProcessor(provider, NullLogger<CommandProcessor>.Instance);
        var command = new DuplicateCommand { MessageId = Guid.NewGuid() };

        var first = await Assert.ThrowsAsync<ArgumentException>(() => processor.SendAsync(command, TestContext.Current.CancellationToken));
        var duplicate = await Assert.ThrowsAsync<ArgumentException>(() => processor.SendAsync(command, TestContext.Current.CancellationToken));

        Assert.Contains("invalid order", first.Message);
        Assert.Contains("invalid order", duplicate.Message);

        Assert.Equal(1, counter.Calls);
    }

    [Fact]
    public async Task SendAsync_WithIdempotency_RerunsHandlerAfterTransientFailure()
    {
        var calls = 0;
        var counter = new Counter
        {
            Throws = () => Interlocked.Increment(ref calls) == 1 ? new IOException("disk busy") : null
        };
        using var provider = BuildProvider(counter, withIdempotency: true);
        await using var processor = new CommandProcessor(provider, NullLogger<CommandProcessor>.Instance);
        var command = new DuplicateCommand { MessageId = Guid.NewGuid() };

        await Assert.ThrowsAsync<IOException>(() => processor.SendAsync(command, TestContext.Current.CancellationToken));
        await processor.SendAsync(command, TestContext.Current.CancellationToken);
        await processor.SendAsync(command, TestContext.Current.CancellationToken);

        Assert.Equal(2, counter.Calls);
    }

    [Fact]
    public async Task SendAsync_WhenIdempotencyStoreFails_RunsHandlerWithoutDeduplication()
    {
        var counter = new Counter();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(counter);
        services.AddSingleton<ICommandHandler<DuplicateCommand>, DuplicateCommandHandler>();
        services.AddSingleton<IIdempotencyStore, FailingStore>();
        services.AddHeroMessaging().WithIdempotency();
        using var provider = services.BuildServiceProvider();
        await using var processor = new CommandProcessor(provider, NullLogger<CommandProcessor>.Instance);
        var command = new DuplicateCommand { MessageId = Guid.NewGuid() };

        await processor.SendAsync(command, TestContext.Current.CancellationToken);
        await processor.SendAsync(command, TestContext.Current.CancellationToken);

        Assert.Equal(2, counter.Calls);
    }

    [Fact]
    public async Task SendAsync_WithIdempotency_CommandWithResponseIsNotDeduplicatedYet()
    {
        var counter = new Counter();
        using var provider = BuildProvider(counter, withIdempotency: true);
        await using var processor = new CommandProcessor(provider, NullLogger<CommandProcessor>.Instance);
        var command = new NumberedCommand { MessageId = Guid.NewGuid() };

        var first = await processor.SendAsync(command, TestContext.Current.CancellationToken);
        var second = await processor.SendAsync(command, TestContext.Current.CancellationToken);

        Assert.Equal(1, first);
        Assert.Equal(2, second);
    }

    [Fact]
    public async Task SendAsync_WithIdempotency_CancelledHandlerIsNotCachedAndPropagates()
    {
        var calls = 0;
        var counter = new Counter
        {
            Throws = () => Interlocked.Increment(ref calls) == 1 ? new OperationCanceledException("stopped") : null
        };
        using var provider = BuildProvider(counter, withIdempotency: true);
        await using var processor = new CommandProcessor(provider, NullLogger<CommandProcessor>.Instance);
        var command = new DuplicateCommand { MessageId = Guid.NewGuid() };

        await Assert.ThrowsAsync<OperationCanceledException>(() => processor.SendAsync(command, TestContext.Current.CancellationToken));
        await processor.SendAsync(command, TestContext.Current.CancellationToken);

        Assert.Equal(2, counter.Calls);
        Assert.Equal(1, processor.GetMetrics().ProcessedCount);
    }

    private static ServiceProvider BuildProvider(Counter counter, bool withIdempotency)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(counter);
        services.AddSingleton<ICommandHandler<DuplicateCommand>, DuplicateCommandHandler>();
        services.AddSingleton<ICommandHandler<NumberedCommand, int>, NumberedCommandHandler>();
        if (withIdempotency)
            services.AddHeroMessaging().WithIdempotency();
        return services.BuildServiceProvider();
    }

    public sealed record DuplicateCommand : ICommand
    {
        public Guid MessageId { get; init; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; init; }
        public string? CausationId { get; init; }
        public Dictionary<string, object>? Metadata { get; init; }
    }

    public sealed record NumberedCommand : ICommand<int>
    {
        public Guid MessageId { get; init; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; init; }
        public string? CausationId { get; init; }
        public Dictionary<string, object>? Metadata { get; init; }
    }

    public sealed class NumberedCommandHandler(Counter counter) : ICommandHandler<NumberedCommand, int>
    {
        public Task<int> HandleAsync(NumberedCommand command, CancellationToken cancellationToken = default)
        {
            counter.Hit();
            return Task.FromResult(counter.Calls);
        }
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

    public sealed class Counter
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Func<Exception?>? Throws { get; init; }

        public void Hit()
        {
            Interlocked.Increment(ref _calls);
            if (Throws?.Invoke() is { } exception)
                throw exception;
        }
    }

    public sealed class DuplicateCommandHandler(Counter counter) : ICommandHandler<DuplicateCommand>
    {
        public Task HandleAsync(DuplicateCommand command, CancellationToken cancellationToken = default)
        {
            counter.Hit();
            return Task.CompletedTask;
        }
    }
}
