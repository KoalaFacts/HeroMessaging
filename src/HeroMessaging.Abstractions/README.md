# HeroMessaging.Abstractions

**Core interfaces and contracts for HeroMessaging.**

## Overview

This package contains all interfaces, base classes, and contracts used throughout the HeroMessaging ecosystem. It's automatically included when you install the main `HeroMessaging` package.

## Installation

```bash
dotnet add package HeroMessaging.Abstractions
```

**Note**: Usually not needed directly - install `HeroMessaging` instead.

### Framework Support

- .NET Standard 2.0
- .NET 6.0
- .NET 7.0
- .NET 8.0
- .NET 9.0

## What's Included

### Message Contracts
- `IMessage`: Base message interface
- `ICommand`: Command pattern
- `IQuery<TResponse>`: Query pattern
- `IEvent`: Event pattern

### Handler Contracts
- `ICommandHandler<TCommand>`
- `ICommandHandler<TCommand, TResponse>`
- `IQueryHandler<TQuery, TResponse>`
- `IEventHandler<TEvent>`

### Saga Contracts
- `ISaga`: Saga instance interface
- `ISagaRepository<TSaga>`: Saga persistence
- `SagaBase`: Base saga implementation

### Storage Contracts
- `IMessageStorage`: Message persistence
- `IInboxStorage`: Inbox pattern
- `IOutboxStorage`: Outbox pattern
- `IQueueStorage`: Queue storage
- `IUnitOfWork`: Transaction support

### Transport Contracts
- `IMessageTransport`: Transport abstraction
- `ITransportConsumer`: Consumer management

### Processing Contracts
- `IMessageProcessor`: Message processing
- `ProcessingContext`: Processing state
- `ProcessingResult`: Processing outcome

## Usage

Implement interfaces to create custom components:

### Custom Command Handler

```csharp
using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Handlers;

public class CreateOrderHandler : ICommandHandler<CreateOrderCommand>
{
    public async Task Handle(CreateOrderCommand command, CancellationToken cancellationToken)
    {
        // Process command
    }
}
```

### Custom Storage Provider

```csharp
using HeroMessaging.Abstractions.Storage;

public class CustomMessageStorage : IMessageStorage
{
    public async Task<Guid> Store(IMessage message, IStorageTransaction? transaction, CancellationToken cancellationToken)
    {
        // Implement storage
    }

    // Implement other interface methods...
}
```

## See Also

- [Main Documentation](../../README.md)
- [Core Library](../HeroMessaging/README.md)

## License

This package is licensed under the MIT License.
