using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Abstractions.Idempotency;
using HeroMessaging.Configuration;
using HeroMessaging.Processing;
using HeroMessaging.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroMessaging.Storage.PostgreSql.Tests;

/// <summary>
/// Command deduplication through <c>WithIdempotency</c> with the PostgreSQL idempotency store:
/// two independently built command processors (two hosts) share one database.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Database", "PostgreSQL")]
[Collection(nameof(PostgreSqlIdempotencyStoreCollection))]
public sealed class PostgreSqlCommandIdempotencyTests(PostgreSqlIdempotencyStoreFixture fixture)
{
    [Fact]
    public async Task SendAsync_RedeliveredToAnotherHost_DoesNotRunTheHandlerAgain()
    {
        var ledger = new Ledger();
        var command = new ReserveStock { MessageId = Guid.NewGuid() };

        using (var hostA = BuildHost(ledger))
        await using (var processorA = new CommandProcessor(hostA, NullLogger<CommandProcessor>.Instance))
        {
            await processorA.SendAsync(command, TestContext.Current.CancellationToken);
        }

        using var hostB = BuildHost(ledger);
        await using var processorB = new CommandProcessor(hostB, NullLogger<CommandProcessor>.Instance);
        await processorB.SendAsync(command, TestContext.Current.CancellationToken);

        Assert.Equal(1, ledger.Reservations);
    }

    [Fact]
    public async Task SendAsync_IdempotentFailureOnOneHost_IsRethrownOnAnotherWithoutRerunningTheHandler()
    {
        var ledger = new Ledger { Reject = true };
        var command = new ReserveStock { MessageId = Guid.NewGuid() };

        using (var hostA = BuildHost(ledger))
        await using (var processorA = new CommandProcessor(hostA, NullLogger<CommandProcessor>.Instance))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => processorA.SendAsync(command, TestContext.Current.CancellationToken));
        }

        using var hostB = BuildHost(ledger);
        await using var processorB = new CommandProcessor(hostB, NullLogger<CommandProcessor>.Instance);
        var replayed = await Assert.ThrowsAsync<InvalidOperationException>(
            () => processorB.SendAsync(command, TestContext.Current.CancellationToken));

        Assert.Equal(1, ledger.Reservations);
        Assert.Contains("out of stock", replayed.Message);
    }

    private ServiceProvider BuildHost(Ledger ledger)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(ledger);
        services.AddSingleton<ICommandHandler<ReserveStock>, ReserveStockHandler>();
        services.AddSingleton<IIdempotencyStore>(_ => new PostgreSqlIdempotencyStore(
            fixture.ConnectionString, TimeProvider.System, new DefaultJsonSerializer(new DefaultBufferPoolManager())));
        services.AddHeroMessaging().WithIdempotency();
        return services.BuildServiceProvider();
    }

    public sealed record ReserveStock : ICommand
    {
        public Guid MessageId { get; init; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; init; }
        public string? CausationId { get; init; }
        public Dictionary<string, object>? Metadata { get; init; }
    }

    public sealed class Ledger
    {
        private int _reservations;
        public bool Reject { get; init; }
        public int Reservations => Volatile.Read(ref _reservations);
        public void Reserve() => Interlocked.Increment(ref _reservations);
    }

    public sealed class ReserveStockHandler(Ledger ledger) : ICommandHandler<ReserveStock>
    {
        public Task HandleAsync(ReserveStock command, CancellationToken cancellationToken = default)
        {
            ledger.Reserve();
            return ledger.Reject
                ? Task.FromException(new InvalidOperationException("SKU-42 is out of stock"))
                : Task.CompletedTask;
        }
    }
}
