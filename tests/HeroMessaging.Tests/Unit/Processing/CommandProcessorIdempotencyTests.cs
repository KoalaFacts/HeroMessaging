using System.Data;
using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Abstractions.Idempotency;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Configuration;
using HeroMessaging.Processing;
using HeroMessaging.Processing.Decorators;
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

    [Fact]
    public async Task SendAsync_InTransactionWhoseCommitFails_RerunsHandlerOnRedelivery()
    {
        var counter = new Counter();
        using var provider = BuildProvider(counter, withIdempotency: true);
        await using var inner = new CommandProcessor(provider, NullLogger<CommandProcessor>.Instance);
        var executor = new CommitFailingExecutor(failingCommits: 1);
        var processor = new TransactionCommandProcessorDecorator(inner, executor);
        var store = provider.GetRequiredService<IIdempotencyStore>();
        var command = new ChargeCard { MessageId = Guid.NewGuid() };
        var key = $"idempotency:{command.MessageId}:handler:{typeof(ChargeCardHandler).FullName}";

        await Assert.ThrowsAsync<IOException>(() => processor.SendAsync(command, TestContext.Current.CancellationToken));
        Assert.False(await store.ExistsAsync(key, TestContext.Current.CancellationToken));

        await processor.SendAsync(command, TestContext.Current.CancellationToken);
        await processor.SendAsync(command, TestContext.Current.CancellationToken);

        Assert.Equal(2, counter.Calls);
        Assert.True(await store.ExistsAsync(key, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SendAsync_InNestedTransactions_RecordsSuccessOnlyAfterOutermostCommit()
    {
        var counter = new Counter();
        using var provider = BuildProvider(counter, withIdempotency: true);
        await using var inner = new CommandProcessor(provider, NullLogger<CommandProcessor>.Instance);
        var store = provider.GetRequiredService<IIdempotencyStore>();
        var command = new ChargeCard { MessageId = Guid.NewGuid() };
        var key = $"idempotency:{command.MessageId}:handler:{typeof(ChargeCardHandler).FullName}";
        var innerProcessor = new TransactionCommandProcessorDecorator(inner, new CommitFailingExecutor(failingCommits: 0));
        var keyStoredBeforeOuterCommit = true;
        var outer = new CommitFailingExecutor(failingCommits: 1)
        {
            BeforeCommit = async () => keyStoredBeforeOuterCommit =
                await store.ExistsAsync(key, TestContext.Current.CancellationToken)
        };

        var processor = new TransactionCommandProcessorDecorator(innerProcessor, outer);

        await Assert.ThrowsAsync<IOException>(() => processor.SendAsync(command, TestContext.Current.CancellationToken));

        Assert.False(keyStoredBeforeOuterCommit);
        Assert.False(await store.ExistsAsync(key, TestContext.Current.CancellationToken));
    }

    /// <summary>Runs the operation, then fails the first <c>failingCommits</c> commits like a rolled-back transaction.</summary>
    private sealed class CommitFailingExecutor(int failingCommits) : ITransactionExecutor
    {
        private int _remainingFailures = failingCommits;

        public Func<Task>? BeforeCommit { get; init; }

        public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, string operationName,
            IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken cancellationToken = default)
        {
            await operation(cancellationToken);
            await CommitAsync();
        }

        public async Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation,
            string operationName, IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
            CancellationToken cancellationToken = default)
        {
            var result = await operation(cancellationToken);
            await CommitAsync();
            return result;
        }

        private async Task CommitAsync()
        {
            if (BeforeCommit is not null)
                await BeforeCommit();
            if (Interlocked.Decrement(ref _remainingFailures) >= 0)
                throw new IOException("commit failed");
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
