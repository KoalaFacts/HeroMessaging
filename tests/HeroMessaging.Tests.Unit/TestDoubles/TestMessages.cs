using System;
using System.Collections.Generic;
using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Queries;

namespace HeroMessaging.Tests.Unit.TestDoubles;

// Base test message implementations
public abstract class TestMessageBase : IMessage
{
    public Guid MessageId { get; set; } = Guid.NewGuid();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object>? Metadata { get; set; }
}

// Test Commands
public class TestCommand : TestMessageBase, ICommand { }

public class TestCommandWithResponse : TestMessageBase, ICommand<string> { }

public class TestCommandWithData : TestMessageBase, ICommand
{
    public string Data { get; set; } = string.Empty;
}

public class TestCommandWithResponseAndData : TestMessageBase, ICommand<TestResponse>
{
    public int Value { get; set; }
}

// Test Queries
public class TestQuery : TestMessageBase, IQuery<string> { }

public class TestQueryWithData : TestMessageBase, IQuery<TestResponse>
{
    public int Id { get; set; }
}

// Test Events
public class TestEvent : TestMessageBase, IEvent { }

public class TestEventWithData : TestMessageBase, IEvent
{
    public string EventData { get; set; } = string.Empty;
}

// Test Messages
public class TestMessage : TestMessageBase
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
}

public class TestQueueMessage : TestMessageBase
{
    public string Content { get; set; } = string.Empty;
    public int Priority { get; set; }
}

// Test Response Types
public class TestResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime Created { get; set; }
}