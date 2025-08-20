using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Queries;

namespace HeroMessaging.Tests.Unit.Fixtures;

// Base test message
public abstract class TestMessageBase : IMessage
{
    public Guid MessageId { get; } = Guid.NewGuid();
    public DateTime Timestamp { get; } = DateTime.UtcNow;
    public Dictionary<string, object>? Metadata { get; set; }
}

// Commands
public class TestCommand : TestMessageBase, ICommand
{
    public string Value { get; set; } = "";
}

public class TestCommandWithResult : TestMessageBase, ICommand<string>
{
    public string Input { get; set; } = "";
}

public class UnhandledCommand : TestMessageBase, ICommand
{
}

public class FailingCommand : TestMessageBase, ICommand
{
    public bool ShouldFail { get; set; }
}

public class SlowCommand : TestMessageBase, ICommand
{
    public int DelayMs { get; set; }
}

// Queries
public class GetUserQuery : TestMessageBase, IQuery<UserDto>
{
    public string UserId { get; set; } = "";
}

public class GetItemsQuery : TestMessageBase, IQuery<List<string>>
{
    public int Count { get; set; }
}

public class UnhandledQuery : TestMessageBase, IQuery<string>
{
}

public class FailingQuery : TestMessageBase, IQuery<string>
{
    public bool ShouldFail { get; set; }
}

public class SlowQuery : TestMessageBase, IQuery<string>
{
    public int DelayMs { get; set; }
}

// Events
public class UserCreatedEvent : TestMessageBase, IEvent
{
    public string UserId { get; set; } = "";
    public string Username { get; set; } = "";
}

public class OrderPlacedEvent : TestMessageBase, IEvent
{
    public string OrderId { get; set; } = "";
}

public class UnhandledEvent : TestMessageBase, IEvent
{
    public string Data { get; set; } = "";
}

public class FailingEvent : TestMessageBase, IEvent
{
    public bool ShouldFail { get; set; }
}

public class SlowEvent : TestMessageBase, IEvent
{
    public int DelayMs { get; set; }
}

// Storage test messages
public class TestMessage : TestMessageBase
{
    public string Content { get; set; } = "";
    public int Priority { get; set; }
}

public class DifferentMessage : TestMessageBase
{
    public string Data { get; set; } = "";
}

// DTOs
public class UserDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}