using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Abstractions.Queries;
using System.Collections.Concurrent;

namespace HeroMessaging.Tests.Unit.Fixtures;

// Command handlers
public class TestCommandHandler : ICommandHandler<TestCommand>
{
    public static int CallCount { get; private set; }
    public static TestCommand? LastCommand { get; private set; }

    public static void ResetCallCount()
    {
        CallCount = 0;
        LastCommand = null;
    }

    public async Task Handle(TestCommand command, CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastCommand = command;
        await Task.CompletedTask;
    }
}

public class TestCommandWithResultHandler : ICommandHandler<TestCommandWithResult, string>
{
    public async Task<string> Handle(TestCommandWithResult command, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        return $"Processed: {command.Input}";
    }
}

public class FailingCommandHandler : ICommandHandler<FailingCommand>
{
    public async Task Handle(FailingCommand command, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        if (command.ShouldFail)
        {
            throw new InvalidOperationException("Command failed");
        }
    }
}

public class SlowCommandHandler : ICommandHandler<SlowCommand>
{
    public async Task Handle(SlowCommand command, CancellationToken cancellationToken = default)
    {
        await Task.Delay(command.DelayMs, cancellationToken);
    }
}

// Query handlers
public class GetUserQueryHandler : IQueryHandler<GetUserQuery, UserDto>
{
    public async Task<UserDto> Handle(GetUserQuery query, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        return new UserDto
        {
            Id = query.UserId,
            Name = $"User {query.UserId}"
        };
    }
}

public class GetItemsQueryHandler : IQueryHandler<GetItemsQuery, List<string>>
{
    public async Task<List<string>> Handle(GetItemsQuery query, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        return Enumerable.Range(0, query.Count)
            .Select(i => $"Item {i}")
            .ToList();
    }
}

public class FailingQueryHandler : IQueryHandler<FailingQuery, string>
{
    public async Task<string> Handle(FailingQuery query, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        if (query.ShouldFail)
        {
            throw new InvalidOperationException("Query failed");
        }
        return "Success";
    }
}

public class SlowQueryHandler : IQueryHandler<SlowQuery, string>
{
    public async Task<string> Handle(SlowQuery query, CancellationToken cancellationToken = default)
    {
        await Task.Delay(query.DelayMs, cancellationToken);
        return "Completed";
    }
}

// Event handlers
public class UserCreatedEmailHandler : IEventHandler<UserCreatedEvent>
{
    private static readonly ConcurrentBag<UserCreatedEvent> _handledEvents = new();
    public static IReadOnlyCollection<UserCreatedEvent> HandledEvents => _handledEvents;
    public static bool ShouldFail { get; set; }

    public static void Reset()
    {
        _handledEvents.Clear();
        ShouldFail = false;
    }

    public async Task Handle(UserCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        if (ShouldFail)
        {
            throw new InvalidOperationException("Email handler failed");
        }
        
        await Task.Delay(10, cancellationToken);
        _handledEvents.Add(@event);
    }
}

public class UserCreatedAuditHandler : IEventHandler<UserCreatedEvent>
{
    private static readonly ConcurrentBag<UserCreatedEvent> _handledEvents = new();
    public static IReadOnlyCollection<UserCreatedEvent> HandledEvents => _handledEvents;

    public static void Reset()
    {
        _handledEvents.Clear();
    }

    public async Task Handle(UserCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        await Task.Delay(5, cancellationToken);
        _handledEvents.Add(@event);
    }
}

public class UserCreatedNotificationHandler : IEventHandler<UserCreatedEvent>
{
    private static readonly ConcurrentBag<UserCreatedEvent> _handledEvents = new();
    public static IReadOnlyCollection<UserCreatedEvent> HandledEvents => _handledEvents;

    public static void Reset()
    {
        _handledEvents.Clear();
    }

    public async Task Handle(UserCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        await Task.Delay(15, cancellationToken);
        _handledEvents.Add(@event);
    }
}

public class OrderPlacedHandler : IEventHandler<OrderPlacedEvent>
{
    private static readonly ConcurrentBag<OrderPlacedEvent> _handledEvents = new();
    public static IReadOnlyCollection<OrderPlacedEvent> HandledEvents => _handledEvents;

    public static void Reset()
    {
        _handledEvents.Clear();
    }

    public async Task Handle(OrderPlacedEvent @event, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        _handledEvents.Add(@event);
    }
}

public class FailingEventHandler : IEventHandler<FailingEvent>
{
    public async Task Handle(FailingEvent @event, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        if (@event.ShouldFail)
        {
            throw new InvalidOperationException("Event handler failed");
        }
    }
}

public class SlowEventHandler : IEventHandler<SlowEvent>
{
    public static int CompletedCount { get; private set; }

    public async Task Handle(SlowEvent @event, CancellationToken cancellationToken = default)
    {
        await Task.Delay(@event.DelayMs, cancellationToken);
        CompletedCount++;
    }
}