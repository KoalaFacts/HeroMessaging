using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeroMessaging.Tests.Integration;

[Trait("Category", "Integration")]
public class QuickStartTests
{
    [Fact]
    public async Task DocumentedRegistration_PublishesToHandler()
    {
        var services = new ServiceCollection();
        var received = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        services.AddSingleton(received);
        services.AddHeroMessaging(builder => builder.Development());
        services.AddTransient<IEventHandler<OrderCreatedEvent>, OrderCreatedHandler>();

        await using var provider = services.BuildServiceProvider();
        var messaging = provider.GetRequiredService<IHeroMessaging>();
        var orderId = Guid.NewGuid();
        await messaging.PublishAsync(new OrderCreatedEvent { OrderId = orderId, Amount = 99.99m },
            TestContext.Current.CancellationToken);

        Assert.Equal(orderId, await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    public sealed record OrderCreatedEvent : IEvent
    {
        public Guid MessageId { get; init; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; init; }
        public string? CausationId { get; init; }
        public Dictionary<string, object>? Metadata { get; init; }
        public Guid OrderId { get; init; }
        public decimal Amount { get; init; }
    }

    public sealed class OrderCreatedHandler(TaskCompletionSource<Guid> received) : IEventHandler<OrderCreatedEvent>
    {
        public Task HandleAsync(OrderCreatedEvent message, CancellationToken cancellationToken = default)
        {
            received.TrySetResult(message.OrderId);
            return Task.CompletedTask;
        }
    }
}
