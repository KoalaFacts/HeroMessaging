using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Abstractions.Queries;
using HeroMessaging.Core;
using HeroMessaging.Core.Configuration;
using HeroMessaging.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HeroMessaging.Tests.Integration.EndToEnd;

public class MessageFlowIntegrationTests : IntegrationTestBase
{
    private IHeroMessaging _messagingService = null!;
    private readonly List<TestEvent> _receivedEvents = new();
    private readonly SemaphoreSlim _eventReceivedSignal = new(0);

    protected override async Task ConfigureServicesAsync(IServiceCollection services)
    {
        services.AddSingleton<ICommandHandler<TestCommand>, TestCommandHandler>();
        services.AddSingleton<ICommandHandler<TestCommandWithResult, string>, TestCommandWithResultHandler>();
        services.AddSingleton<IQueryHandler<TestQuery, TestQueryResult>, TestQueryHandler>();
        services.AddSingleton<IEventHandler<TestEvent>, TestEventHandler>();
        services.AddSingleton(provider => new TestEventHandler(_receivedEvents, _eventReceivedSignal));

        var builder = HeroMessagingBuilder.Create(services)
            .WithMediator()
            .WithEventBus()
            .WithQueues()
            .WithOutbox()
            .WithInbox();

        builder.RegisterHandlers(typeof(MessageFlowIntegrationTests).Assembly);
        
        services.AddSingleton<IHeroMessaging>(provider => 
            new HeroMessagingService(builder.Build(provider)));

        await base.ConfigureServicesAsync(services);
    }

    protected override Task OnInitializedAsync()
    {
        _messagingService = GetRequiredService<IHeroMessaging>();
        return base.OnInitializedAsync();
    }

    [Fact]
    public async Task Should_Execute_Command_Successfully()
    {
        // Arrange
        var command = new TestCommand { Id = Guid.NewGuid(), Name = "Test Command" };

        // Act
        await _messagingService.Send(command);

        // Assert
        // Command executed without throwing exception
        Assert.True(true);
    }

    [Fact]
    public async Task Should_Execute_Command_With_Result_Successfully()
    {
        // Arrange
        var command = new TestCommandWithResult { Id = Guid.NewGuid(), Value = "Test Value" };

        // Act
        var result = await _messagingService.Send(command);

        // Assert
        Assert.Equal($"Processed: {command.Value}", result);
    }

    [Fact]
    public async Task Should_Execute_Query_Successfully()
    {
        // Arrange
        var query = new TestQuery { Id = Guid.NewGuid(), Filter = "Test Filter" };

        // Act
        var result = await _messagingService.Send(query);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(query.Id, result.Id);
        Assert.Equal($"Result for: {query.Filter}", result.Data);
    }

    [Fact]
    public async Task Should_Publish_Event_To_Multiple_Handlers()
    {
        // Arrange
        var @event = new TestEvent { Id = Guid.NewGuid(), Message = "Test Event" };

        // Act
        await _messagingService.Publish(@event);
        
        // Wait for event to be processed (with timeout)
        var received = await _eventReceivedSignal.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(received);
        Assert.Contains(_receivedEvents, e => e.Id == @event.Id);
    }

    [Fact]
    public async Task Should_Handle_Multiple_Commands_Concurrently()
    {
        // Arrange
        var tasks = new List<Task>();
        var commandCount = 100;

        // Act
        for (int i = 0; i < commandCount; i++)
        {
            var command = new TestCommand { Id = Guid.NewGuid(), Name = $"Command {i}" };
            tasks.Add(_messagingService.Send(command));
        }

        await Task.WhenAll(tasks);

        // Assert
        // All commands executed without throwing exception
        Assert.True(true);
    }

    [Fact]
    public async Task Should_Handle_Command_Failure_Gracefully()
    {
        // Arrange
        var command = new TestCommand { Id = Guid.NewGuid(), Name = "FAIL" }; // Special name to trigger failure

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _messagingService.Send(command));
    }
}

// Test Messages
public class TestCommand : ICommand
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid MessageId { get; } = Guid.NewGuid();
    public DateTime Timestamp { get; } = DateTime.UtcNow;
    public Dictionary<string, object>? Metadata { get; set; }
}

public class TestCommandWithResult : ICommand<string>
{
    public Guid Id { get; set; }
    public string Value { get; set; } = string.Empty;
    public Guid MessageId { get; } = Guid.NewGuid();
    public DateTime Timestamp { get; } = DateTime.UtcNow;
    public Dictionary<string, object>? Metadata { get; set; }
}

public class TestQuery : IQuery<TestQueryResult>
{
    public Guid Id { get; set; }
    public string Filter { get; set; } = string.Empty;
    public Guid MessageId { get; } = Guid.NewGuid();
    public DateTime Timestamp { get; } = DateTime.UtcNow;
    public Dictionary<string, object>? Metadata { get; set; }
}

public class TestQueryResult
{
    public Guid Id { get; set; }
    public string Data { get; set; } = string.Empty;
}

public class TestEvent : IEvent
{
    public Guid Id { get; set; }
    public string Message { get; set; } = string.Empty;
    public Guid MessageId { get; } = Guid.NewGuid();
    public DateTime Timestamp { get; } = DateTime.UtcNow;
    public Dictionary<string, object>? Metadata { get; set; }
}

// Test Handlers
public class TestCommandHandler : ICommandHandler<TestCommand>
{
    public Task Handle(TestCommand command, CancellationToken cancellationToken = default)
    {
        if (command.Name == "FAIL")
        {
            throw new InvalidOperationException("Command failed as requested");
        }
        
        return Task.CompletedTask;
    }
}

public class TestCommandWithResultHandler : ICommandHandler<TestCommandWithResult, string>
{
    public Task<string> Handle(TestCommandWithResult command, CancellationToken cancellationToken = default)
    {
        return Task.FromResult($"Processed: {command.Value}");
    }
}

public class TestQueryHandler : IQueryHandler<TestQuery, TestQueryResult>
{
    public Task<TestQueryResult> Handle(TestQuery query, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new TestQueryResult
        {
            Id = query.Id,
            Data = $"Result for: {query.Filter}"
        });
    }
}

public class TestEventHandler : IEventHandler<TestEvent>
{
    private readonly List<TestEvent> _receivedEvents;
    private readonly SemaphoreSlim _signal;

    public TestEventHandler(List<TestEvent> receivedEvents, SemaphoreSlim signal)
    {
        _receivedEvents = receivedEvents;
        _signal = signal;
    }

    public Task Handle(TestEvent @event, CancellationToken cancellationToken = default)
    {
        _receivedEvents.Add(@event);
        _signal.Release();
        return Task.CompletedTask;
    }
}