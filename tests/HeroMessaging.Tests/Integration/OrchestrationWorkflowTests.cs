using HeroMessaging.Abstractions.Sagas;
using HeroMessaging.Orchestration;
using HeroMessaging.Tests.Examples;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroMessaging.Tests.Integration;

[Trait("Category", "Integration")]
public class OrchestrationWorkflowTests
{
    [Fact]
    public async Task OrderSaga_HappyPath_CompletesSuccessfully()
    {
        // Arrange
        var repository = new InMemorySagaRepository<OrderSaga>();
        var stateMachine = OrderSagaStateMachine.Build();
        var services = new ServiceCollection().BuildServiceProvider();
        var logger = NullLogger<SagaOrchestrator<OrderSaga>>.Instance;
        var orchestrator = new SagaOrchestrator<OrderSaga>(repository, stateMachine, services, logger);

        var correlationId = Guid.NewGuid();
        var orderId = $"ORDER-{Guid.NewGuid()}";
        var customerId = $"CUST-{Guid.NewGuid()}";

        // Act - Complete order workflow
        var orderCreated = new OrderCreatedEvent(
            orderId,
            customerId,
            TotalAmount: 99.99m,
            Items: new List<OrderItem>
            {
                new() { ProductId = "PROD-1", Quantity = 2, Price = 49.99m }
            })
        {
            CorrelationId = correlationId.ToString()
        };
        await orchestrator.ProcessAsync(orderCreated);

        var paymentProcessed = new PaymentProcessedEvent(
            orderId,
            TransactionId: "TXN-123",
            Amount: 99.99m)
        {
            CorrelationId = correlationId.ToString()
        };
        await orchestrator.ProcessAsync(paymentProcessed);

        var inventoryReserved = new InventoryReservedEvent(
            orderId,
            ReservationId: "RES-456",
            Items: orderCreated.Items)
        {
            CorrelationId = correlationId.ToString()
        };
        await orchestrator.ProcessAsync(inventoryReserved);

        var orderShipped = new OrderShippedEvent(
            orderId,
            TrackingNumber: "TRACK-789")
        {
            CorrelationId = correlationId.ToString()
        };
        await orchestrator.ProcessAsync(orderShipped);

        // Assert
        var saga = await repository.FindAsync(correlationId);
        Assert.NotNull(saga);
        Assert.Equal("Completed", saga!.CurrentState);
        Assert.True(saga.IsCompleted);
        Assert.Equal(orderId, saga.OrderId);
        Assert.Equal(customerId, saga.CustomerId);
        Assert.Equal(99.99m, saga.TotalAmount);
        Assert.Equal("TXN-123", saga.PaymentTransactionId);
        Assert.Equal("RES-456", saga.InventoryReservationId);
        Assert.Equal("TRACK-789", saga.ShipmentTrackingNumber);
    }

    [Fact]
    public async Task OrderSaga_PaymentFailed_TransitionsToFailed()
    {
        // Arrange
        var repository = new InMemorySagaRepository<OrderSaga>();
        var stateMachine = OrderSagaStateMachine.Build();
        var services = new ServiceCollection().BuildServiceProvider();
        var logger = NullLogger<SagaOrchestrator<OrderSaga>>.Instance;
        var orchestrator = new SagaOrchestrator<OrderSaga>(repository, stateMachine, services, logger);

        var correlationId = Guid.NewGuid();
        var orderId = $"ORDER-{Guid.NewGuid()}";

        // Act - Order created, payment fails
        var orderCreated = new OrderCreatedEvent(
            orderId,
            CustomerId: "CUST-123",
            TotalAmount: 99.99m,
            Items: new List<OrderItem>())
        {
            CorrelationId = correlationId.ToString()
        };
        await orchestrator.ProcessAsync(orderCreated);

        var paymentFailed = new PaymentFailedEvent(
            orderId,
            Reason: "Insufficient funds")
        {
            CorrelationId = correlationId.ToString()
        };
        await orchestrator.ProcessAsync(paymentFailed);

        // Assert
        var saga = await repository.FindAsync(correlationId);
        Assert.NotNull(saga);
        Assert.Equal("Failed", saga!.CurrentState);
        Assert.Equal("Insufficient funds", saga.FailureReason);
        Assert.False(saga.IsCompleted); // Failed, not completed
    }

    [Fact]
    public async Task OrderSaga_InventoryFailed_TransitionsToFailedWithCompensation()
    {
        // Arrange
        var repository = new InMemorySagaRepository<OrderSaga>();
        var stateMachine = OrderSagaStateMachine.Build();
        var services = new ServiceCollection().BuildServiceProvider();
        var logger = NullLogger<SagaOrchestrator<OrderSaga>>.Instance;
        var orchestrator = new SagaOrchestrator<OrderSaga>(repository, stateMachine, services, logger);

        var correlationId = Guid.NewGuid();
        var orderId = $"ORDER-{Guid.NewGuid()}";

        // Act - Order created, payment processed, inventory fails
        await orchestrator.ProcessAsync(new OrderCreatedEvent(
            orderId, "CUST-123", 99.99m, new List<OrderItem>())
        { CorrelationId = correlationId.ToString() });

        await orchestrator.ProcessAsync(new PaymentProcessedEvent(
            orderId, "TXN-123", 99.99m)
        { CorrelationId = correlationId.ToString() });

        await orchestrator.ProcessAsync(new InventoryReservationFailedEvent(
            orderId, "Out of stock")
        { CorrelationId = correlationId.ToString() });

        // Assert
        var saga = await repository.FindAsync(correlationId);
        Assert.NotNull(saga);
        Assert.Equal("Failed", saga!.CurrentState);
        Assert.Equal("Out of stock", saga.FailureReason);
        Assert.Equal("TXN-123", saga.PaymentTransactionId); // Payment was processed
        Assert.Null(saga.InventoryReservationId); // Inventory never reserved
    }

    [Fact]
    public async Task OrderSaga_ShipmentFailed_TransitionsToFailedWithFullCompensation()
    {
        // Arrange
        var repository = new InMemorySagaRepository<OrderSaga>();
        var stateMachine = OrderSagaStateMachine.Build();
        var services = new ServiceCollection().BuildServiceProvider();
        var logger = NullLogger<SagaOrchestrator<OrderSaga>>.Instance;
        var orchestrator = new SagaOrchestrator<OrderSaga>(repository, stateMachine, services, logger);

        var correlationId = Guid.NewGuid();
        var orderId = $"ORDER-{Guid.NewGuid()}";

        // Act - Complete workflow until shipment fails
        await orchestrator.ProcessAsync(new OrderCreatedEvent(
            orderId, "CUST-123", 99.99m, new List<OrderItem>())
        { CorrelationId = correlationId.ToString() });

        await orchestrator.ProcessAsync(new PaymentProcessedEvent(
            orderId, "TXN-123", 99.99m)
        { CorrelationId = correlationId.ToString() });

        await orchestrator.ProcessAsync(new InventoryReservedEvent(
            orderId, "RES-456", new List<OrderItem>())
        { CorrelationId = correlationId.ToString() });

        await orchestrator.ProcessAsync(new ShipmentFailedEvent(
            orderId, "Carrier unavailable")
        { CorrelationId = correlationId.ToString() });

        // Assert
        var saga = await repository.FindAsync(correlationId);
        Assert.NotNull(saga);
        Assert.Equal("Failed", saga!.CurrentState);
        Assert.Equal("Carrier unavailable", saga.FailureReason);
        Assert.Equal("TXN-123", saga.PaymentTransactionId);
        Assert.Equal("RES-456", saga.InventoryReservationId);
        Assert.Null(saga.ShipmentTrackingNumber);
    }

    [Fact]
    public async Task OrderSaga_CancelledDuringPayment_TransitionsToCancelled()
    {
        // Arrange
        var repository = new InMemorySagaRepository<OrderSaga>();
        var stateMachine = OrderSagaStateMachine.Build();
        var services = new ServiceCollection().BuildServiceProvider();
        var logger = NullLogger<SagaOrchestrator<OrderSaga>>.Instance;
        var orchestrator = new SagaOrchestrator<OrderSaga>(repository, stateMachine, services, logger);

        var correlationId = Guid.NewGuid();
        var orderId = $"ORDER-{Guid.NewGuid()}";

        // Act
        await orchestrator.ProcessAsync(new OrderCreatedEvent(
            orderId, "CUST-123", 99.99m, new List<OrderItem>())
        { CorrelationId = correlationId.ToString() });

        await orchestrator.ProcessAsync(new OrderCancelledEvent(
            orderId, "Customer requested cancellation")
        { CorrelationId = correlationId.ToString() });

        // Assert
        var saga = await repository.FindAsync(correlationId);
        Assert.NotNull(saga);
        Assert.Equal("Cancelled", saga!.CurrentState);
        Assert.True(saga.IsCompleted); // Cancelled is a final state
        Assert.Equal("Customer requested cancellation", saga.FailureReason);
    }

    [Fact]
    public async Task OrderSaga_CancelledAfterPayment_CompensatesPayment()
    {
        // Arrange
        var repository = new InMemorySagaRepository<OrderSaga>();
        var stateMachine = OrderSagaStateMachine.Build();
        var services = new ServiceCollection().BuildServiceProvider();
        var logger = NullLogger<SagaOrchestrator<OrderSaga>>.Instance;
        var orchestrator = new SagaOrchestrator<OrderSaga>(repository, stateMachine, services, logger);

        var correlationId = Guid.NewGuid();
        var orderId = $"ORDER-{Guid.NewGuid()}";

        // Act
        await orchestrator.ProcessAsync(new OrderCreatedEvent(
            orderId, "CUST-123", 99.99m, new List<OrderItem>())
        { CorrelationId = correlationId.ToString() });

        await orchestrator.ProcessAsync(new PaymentProcessedEvent(
            orderId, "TXN-123", 99.99m)
        { CorrelationId = correlationId.ToString() });

        await orchestrator.ProcessAsync(new OrderCancelledEvent(
            orderId, "Customer changed mind")
        { CorrelationId = correlationId.ToString() });

        // Assert
        var saga = await repository.FindAsync(correlationId);
        Assert.NotNull(saga);
        Assert.Equal("Cancelled", saga!.CurrentState);
        Assert.True(saga.IsCompleted);
        Assert.Equal("TXN-123", saga.PaymentTransactionId);
    }

    [Fact]
    public async Task OrderSaga_MultipleInstancesConcurrently_ProcessIndependently()
    {
        // Arrange
        var repository = new InMemorySagaRepository<OrderSaga>();
        var stateMachine = OrderSagaStateMachine.Build();
        var services = new ServiceCollection().BuildServiceProvider();
        var logger = NullLogger<SagaOrchestrator<OrderSaga>>.Instance;
        var orchestrator = new SagaOrchestrator<OrderSaga>(repository, stateMachine, services, logger);

        var correlation1 = Guid.NewGuid();
        var correlation2 = Guid.NewGuid();
        var correlation3 = Guid.NewGuid();

        // Act - Process three orders concurrently
        var tasks = new List<Task>
        {
            ProcessFullOrder(orchestrator, correlation1, "ORDER-1"),
            ProcessFullOrder(orchestrator, correlation2, "ORDER-2"),
            ProcessFullOrder(orchestrator, correlation3, "ORDER-3")
        };

        await Task.WhenAll(tasks);

        // Assert - All three orders completed independently
        var saga1 = await repository.FindAsync(correlation1);
        var saga2 = await repository.FindAsync(correlation2);
        var saga3 = await repository.FindAsync(correlation3);

        Assert.NotNull(saga1);
        Assert.NotNull(saga2);
        Assert.NotNull(saga3);

        Assert.Equal("Completed", saga1!.CurrentState);
        Assert.Equal("Completed", saga2!.CurrentState);
        Assert.Equal("Completed", saga3!.CurrentState);

        Assert.Equal("ORDER-1", saga1.OrderId);
        Assert.Equal("ORDER-2", saga2.OrderId);
        Assert.Equal("ORDER-3", saga3.OrderId);
    }

    [Fact]
    public async Task OrderSaga_StateTransitions_UpdateTimestamps()
    {
        // Arrange
        var repository = new InMemorySagaRepository<OrderSaga>();
        var stateMachine = OrderSagaStateMachine.Build();
        var services = new ServiceCollection().BuildServiceProvider();
        var logger = NullLogger<SagaOrchestrator<OrderSaga>>.Instance;
        var orchestrator = new SagaOrchestrator<OrderSaga>(repository, stateMachine, services, logger);

        var correlationId = Guid.NewGuid();
        var orderId = "ORDER-123";

        // Act
        await orchestrator.ProcessAsync(new OrderCreatedEvent(
            orderId, "CUST-123", 99.99m, new List<OrderItem>())
        { CorrelationId = correlationId.ToString() });

        var sagaAfterCreation = await repository.FindAsync(correlationId);
        var createdTime = sagaAfterCreation!.UpdatedAt;

        await Task.Delay(10); // Small delay to ensure timestamp difference

        await orchestrator.ProcessAsync(new PaymentProcessedEvent(
            orderId, "TXN-123", 99.99m)
        { CorrelationId = correlationId.ToString() });

        var sagaAfterPayment = await repository.FindAsync(correlationId);

        // Assert
        Assert.True(sagaAfterPayment!.UpdatedAt > createdTime);
        Assert.Equal(sagaAfterCreation.CreatedAt, sagaAfterPayment.CreatedAt); // Created time unchanged
    }

    private static async Task ProcessFullOrder(
        SagaOrchestrator<OrderSaga> orchestrator,
        Guid correlationId,
        string orderId)
    {
        await orchestrator.ProcessAsync(new OrderCreatedEvent(
            orderId, "CUST-123", 99.99m, new List<OrderItem>())
        { CorrelationId = correlationId.ToString() });

        await orchestrator.ProcessAsync(new PaymentProcessedEvent(
            orderId, $"TXN-{orderId}", 99.99m)
        { CorrelationId = correlationId.ToString() });

        await orchestrator.ProcessAsync(new InventoryReservedEvent(
            orderId, $"RES-{orderId}", new List<OrderItem>())
        { CorrelationId = correlationId.ToString() });

        await orchestrator.ProcessAsync(new OrderShippedEvent(
            orderId, $"TRACK-{orderId}")
        { CorrelationId = correlationId.ToString() });
    }
}
