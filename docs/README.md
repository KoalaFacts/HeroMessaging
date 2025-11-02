# HeroMessaging Documentation

This directory contains comprehensive documentation for HeroMessaging.

## Quick Links

### User Documentation
- **[Saga Orchestration Pattern](orchestration-pattern.md)** - Complete guide to implementing sagas with state machines
- **[Choreography Pattern](choreography-pattern.md)** - Event-driven choreography for distributed systems
- **[OpenTelemetry Integration](opentelemetry-integration.md)** - Distributed tracing and observability setup
- **[Builder API Guide](builder-api-guide.md)** - Fluent API for configuring HeroMessaging

### Developer Documentation
- **[Testing Guide](testing-guide.md)** - Testing infrastructure, patterns, and guidelines
- **[Architecture Decision Records (ADRs)](adr/)** - Design decisions and rationale

## Architecture Decision Records

ADRs document significant architectural decisions:

- **[0001-message-scheduling.md](adr/0001-message-scheduling.md)** - Message scheduling approach
- **[0002-transport-abstraction-layer.md](adr/0002-transport-abstraction-layer.md)** - Transport abstraction design
- **[0003-rabbitmq-transport.md](adr/0003-rabbitmq-transport.md)** - RabbitMQ integration decisions
- **[0004-saga-patterns.md](adr/0004-saga-patterns.md)** - Saga patterns (orchestration vs choreography)

## Getting Started

New to HeroMessaging? Start here:

1. **[Main README](../README.md)** - Project overview and quick start
2. **[Contributing Guide](../CONTRIBUTING.md)** - Development workflow and standards
3. **[Testing Guide](testing-guide.md)** - Testing approach and infrastructure

## Pattern Guides

### Saga Orchestration

The saga pattern coordinates long-running distributed transactions:

```csharp
// Define saga
public class OrderSaga : SagaBase
{
    public Guid OrderId { get; set; }
    public string State { get; set; } = "Pending";
}

// Build state machine
var stateMachine = new StateMachineBuilder<OrderSaga>()
    .Initially(b => b
        .When<OrderCreatedEvent>()
        .Then((saga, evt) => saga.OrderId = evt.OrderId)
        .TransitionTo("Processing"))
    .Build();

// Register
services.AddHeroMessaging(builder =>
{
    builder.AddSaga<OrderSaga>(OrderSagaStateMachine.Build);
    builder.UseInMemorySagaRepository<OrderSaga>();
});
```

See [orchestration-pattern.md](orchestration-pattern.md) for complete guide.

### Event Choreography

Event-driven coordination without a central orchestrator:

```csharp
public class OrderCreatedHandler : IMessageHandler<OrderCreatedEvent>
{
    public async Task HandleAsync(OrderCreatedEvent evt, CancellationToken ct)
    {
        // Process order, publish more events
        await _messageBus.PublishAsync(new PaymentRequested(evt.OrderId));
    }
}
```

See [choreography-pattern.md](choreography-pattern.md) for complete guide.

## Observability

### OpenTelemetry

HeroMessaging integrates with OpenTelemetry for distributed tracing:

```csharp
services.AddOpenTelemetry()
    .WithTracing(builder => builder
        .AddHeroMessagingInstrumentation()
        .AddOtlpExporter());
```

See [opentelemetry-integration.md](opentelemetry-integration.md) for setup details.

## Testing

HeroMessaging includes comprehensive testing infrastructure:

- **Unit Tests**: Fast, isolated tests with 80%+ coverage
- **Integration Tests**: Database and service integration
- **Performance Benchmarks**: BenchmarkDotNet for performance validation
- **Contract Tests**: API contract validation

See [testing-guide.md](testing-guide.md) for testing patterns and infrastructure.

## Contributing Documentation

When adding documentation:

1. **User Guides**: Add to `docs/` root (e.g., `new-feature-guide.md`)
2. **Architecture Decisions**: Add to `docs/adr/` with sequential numbering
3. **API Documentation**: Use XML comments on public APIs
4. **Examples**: Include code examples in test project

See [CONTRIBUTING.md](../CONTRIBUTING.md) for full guidelines.

## Archive

Historical analysis and refactoring documents are in [archive/](archive/).
