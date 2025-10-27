using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Choreography;
using HeroMessaging.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeroMessaging.Tests.Integration;

/// <summary>
/// Integration tests demonstrating choreography pattern with correlation tracking
/// </summary>
public class ChoreographyWorkflowTests
{

    [Fact]
    [Trait("Category", "Integration")]
    public async Task OrderWorkflow_WithChoreography_PropagatesCorrelation()
    {
        // Arrange - Set up a complete order processing workflow
        var services = new ServiceCollection();
        var capturedEvents = new List<IMessage>();

        services.AddHeroMessaging(builder => builder
            .WithMediator()     // Registers ICommandProcessor and IQueryProcessor
            .WithEventBus()     // Registers IEventBus
            .ScanAssembly(typeof(ChoreographyWorkflowTests).Assembly));

        // Register event tracker
        services.AddSingleton(capturedEvents);

        // Register workflow handlers
        services.AddTransient<IEventHandler<OrderCreatedEvent>, ReserveInventoryHandler>();
        services.AddTransient<IEventHandler<InventoryReservedEvent>, ProcessPaymentHandler>();
        services.AddTransient<IEventHandler<PaymentProcessedEvent>, ShipOrderHandler>();

        var serviceProvider = services.BuildServiceProvider();
        var messaging = serviceProvider.GetRequiredService<IHeroMessaging>();

        // Act - Start the workflow with an initial event
        var initialCorrelationId = Guid.NewGuid().ToString();
        var orderCreatedEvent = new OrderCreatedEvent
        {
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            TotalAmount = 99.99m,
            CorrelationId = initialCorrelationId
        };

        await messaging.Publish(orderCreatedEvent);

        // Allow async processing to complete
        await Task.Delay(500);

        // Assert - All events in the workflow should have the same CorrelationId
        Assert.True(capturedEvents.Count >= 3, $"Expected at least 3 events, got {capturedEvents.Count}");

        foreach (var capturedEvent in capturedEvents)
        {
            // All events should share the same correlation ID
            Assert.Equal(initialCorrelationId, capturedEvent.CorrelationId);
        }

        // Verify causation chain
        var inventoryEvent = capturedEvents.OfType<InventoryReservedEvent>().FirstOrDefault();
        var paymentEvent = capturedEvents.OfType<PaymentProcessedEvent>().FirstOrDefault();
        var shippingEvent = capturedEvents.OfType<OrderShippedEvent>().FirstOrDefault();

        Assert.NotNull(inventoryEvent);
        Assert.NotNull(paymentEvent);
        Assert.NotNull(shippingEvent);

        // Each event's causation should link back to the previous event
        Assert.Equal(orderCreatedEvent.MessageId.ToString(), inventoryEvent.CausationId);
        Assert.Equal(inventoryEvent.MessageId.ToString(), paymentEvent.CausationId);
        Assert.Equal(paymentEvent.MessageId.ToString(), shippingEvent.CausationId);
    }

    #region Test Workflow Messages

    private record OrderCreatedEvent : MessageBase, IEvent
    {
        public Guid OrderId { get; init; }
        public Guid CustomerId { get; init; }
        public decimal TotalAmount { get; init; }
    }

    private record InventoryReservedEvent : MessageBase, IEvent
    {
        public Guid OrderId { get; init; }
        public Guid ReservationId { get; init; }
    }

    private record PaymentProcessedEvent : MessageBase, IEvent
    {
        public Guid OrderId { get; init; }
        public Guid TransactionId { get; init; }
        public decimal Amount { get; init; }
    }

    private record OrderShippedEvent : MessageBase, IEvent
    {
        public Guid OrderId { get; init; }
        public Guid ShipmentId { get; init; }
        public string TrackingNumber { get; init; } = string.Empty;
    }

    #endregion

    #region Test Handlers

    private class ReserveInventoryHandler : IEventHandler<OrderCreatedEvent>
    {
        private readonly IHeroMessaging _messaging;
        private readonly List<IMessage> _capturedEvents;

        public ReserveInventoryHandler(
            IHeroMessaging messaging,
            List<IMessage> capturedEvents)
        {
            _messaging = messaging;
            _capturedEvents = capturedEvents;
        }

        public async Task Handle(OrderCreatedEvent @event, CancellationToken cancellationToken)
        {
            // Simulate inventory reservation
            await Task.Delay(10, cancellationToken);

            // Publish next event in choreography with automatic correlation
            var inventoryEvent = new InventoryReservedEvent
            {
                OrderId = @event.OrderId,
                ReservationId = Guid.NewGuid()
            }.WithCorrelation(); // Automatically applies correlation context

            _capturedEvents.Add(inventoryEvent);
            await _messaging.Publish(inventoryEvent, cancellationToken);
        }
    }

    private class ProcessPaymentHandler : IEventHandler<InventoryReservedEvent>
    {
        private readonly IHeroMessaging _messaging;
        private readonly List<IMessage> _capturedEvents;

        public ProcessPaymentHandler(
            IHeroMessaging messaging,
            List<IMessage> capturedEvents)
        {
            _messaging = messaging;
            _capturedEvents = capturedEvents;
        }

        public async Task Handle(InventoryReservedEvent @event, CancellationToken cancellationToken)
        {
            // Simulate payment processing
            await Task.Delay(10, cancellationToken);

            var paymentEvent = new PaymentProcessedEvent
            {
                OrderId = @event.OrderId,
                TransactionId = Guid.NewGuid(),
                Amount = 99.99m
            }.WithCorrelation(); // Automatically applies correlation context

            _capturedEvents.Add(paymentEvent);
            await _messaging.Publish(paymentEvent, cancellationToken);
        }
    }

    private class ShipOrderHandler : IEventHandler<PaymentProcessedEvent>
    {
        private readonly IHeroMessaging _messaging;
        private readonly List<IMessage> _capturedEvents;

        public ShipOrderHandler(
            IHeroMessaging messaging,
            List<IMessage> capturedEvents)
        {
            _messaging = messaging;
            _capturedEvents = capturedEvents;
        }

        public async Task Handle(PaymentProcessedEvent @event, CancellationToken cancellationToken)
        {
            // Simulate shipping
            await Task.Delay(10, cancellationToken);

            var shippingEvent = new OrderShippedEvent
            {
                OrderId = @event.OrderId,
                ShipmentId = Guid.NewGuid(),
                TrackingNumber = $"TRACK-{Guid.NewGuid():N}"
            }.WithCorrelation(); // Automatically applies correlation context

            _capturedEvents.Add(shippingEvent);
            await _messaging.Publish(shippingEvent, cancellationToken);
        }
    }

    #endregion
}
