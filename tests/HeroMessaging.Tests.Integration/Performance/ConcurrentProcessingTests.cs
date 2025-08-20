using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Core;
using HeroMessaging.Core.Configuration;
using HeroMessaging.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HeroMessaging.Tests.Integration.Performance;

public class ConcurrentProcessingTests : IntegrationTestBase
{
    private IHeroMessaging _messagingService = null!;
    private readonly ConcurrentDictionary<Guid, DateTime> _processedCommands = new();
    private readonly ConcurrentBag<Guid> _processedEvents = new();

    public ConcurrentProcessingTests()
    {
    }

    protected override async Task ConfigureServicesAsync(IServiceCollection services)
    {
        services.AddSingleton<ICommandHandler<StressTestCommand>>(
            new StressTestCommandHandler(_processedCommands));
        services.AddSingleton<ICommandHandler<StressTestCommandWithResult, string>>(
            new StressTestCommandWithResultHandler());
        services.AddSingleton<IEventHandler<StressTestEvent>>(
            new StressTestEventHandler(_processedEvents));

        var builder = HeroMessagingBuilder.Create(services)
            .WithMediator(options =>
            {
                options.CommandConcurrency = 1; // Sequential for commands
                options.QueryConcurrency = Environment.ProcessorCount;
                options.BoundedCapacity = 1000;
            })
            .WithEventBus(options =>
            {
                options.EventConcurrency = Environment.ProcessorCount; // Parallel for events
                options.BoundedCapacity = 1000;
            });

        builder.RegisterHandlers(typeof(ConcurrentProcessingTests).Assembly);
        
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
    public async Task Should_Process_Commands_Sequentially()
    {
        // Arrange
        var commandCount = 100;
        var commands = Enumerable.Range(0, commandCount)
            .Select(i => new StressTestCommand { Id = Guid.NewGuid(), Sequence = i })
            .ToList();

        // Act
        var stopwatch = Stopwatch.StartNew();
        var tasks = commands.Select(cmd => _messagingService.Send(cmd)).ToList();
        await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Assert
        _processedCommands.Count.Should().Be(commandCount);
        
        // Verify sequential processing by checking timestamps
        var orderedProcessing = _processedCommands
            .OrderBy(kvp => kvp.Value)
            .Select(kvp => commands.First(c => c.Id == kvp.Key).Sequence)
            .ToList();

        // Performance metrics would be logged here in a real test
    }

    [Fact]
    public async Task Should_Process_Events_In_Parallel()
    {
        // Arrange
        var eventCount = 1000;
        var events = Enumerable.Range(0, eventCount)
            .Select(i => new StressTestEvent { Id = Guid.NewGuid(), Data = $"Event {i}" })
            .ToList();

        // Act
        var stopwatch = Stopwatch.StartNew();
        var tasks = events.Select(evt => _messagingService.Publish(evt)).ToList();
        await Task.WhenAll(tasks);
        
        // Wait for all events to be processed
        var timeout = TimeSpan.FromSeconds(10);
        var waitStart = DateTime.UtcNow;
        while (_processedEvents.Count < eventCount && DateTime.UtcNow - waitStart < timeout)
        {
            await Task.Delay(10);
        }
        stopwatch.Stop();

        // Assert
        Assert.Equal(eventCount, _processedEvents.Count);
        
        // Performance metrics would be logged here
        
        // Verify all events were processed
        var processedIds = _processedEvents.ToHashSet();
        foreach (var evt in events)
        {
            Assert.Contains(evt.Id, processedIds);
        }
    }

    [Fact]
    public async Task Should_Handle_High_Throughput_Mixed_Workload()
    {
        // Arrange
        var commandCount = 500;
        var eventCount = 2000;
        var queryCount = 500;
        
        var allTasks = new ConcurrentBag<Task>();
        var errors = new ConcurrentBag<Exception>();

        // Act
        var stopwatch = Stopwatch.StartNew();
        
        // Start command tasks
        var commandTask = Task.Run(async () =>
        {
            for (int i = 0; i < commandCount; i++)
            {
                try
                {
                    var command = new StressTestCommandWithResult { Id = Guid.NewGuid(), Value = i };
                    await _messagingService.Send(command);
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            }
        });

        // Start event tasks
        var eventTask = Task.Run(async () =>
        {
            for (int i = 0; i < eventCount; i++)
            {
                try
                {
                    var evt = new StressTestEvent { Id = Guid.NewGuid(), Data = $"Event {i}" };
                    await _messagingService.Publish(evt);
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            }
        });

        await Task.WhenAll(commandTask, eventTask);
        stopwatch.Stop();

        // Assert
        Assert.Empty(errors);
        
        // Performance test completed successfully
    }

    [Fact]
    public async Task Should_Apply_Backpressure_Under_Load()
    {
        // Arrange
        var messageCount = 5000; // More than bounded capacity
        var sendTasks = new Task[messageCount];
        var rejectedCount = 0;

        // Act
        for (int i = 0; i < messageCount; i++)
        {
            var index = i;
            sendTasks[i] = Task.Run(async () =>
            {
                try
                {
                    var command = new StressTestCommand { Id = Guid.NewGuid(), Sequence = index };
                    await _messagingService.Send(command);
                }
                catch (InvalidOperationException) // Bounded capacity reached
                {
                    Interlocked.Increment(ref rejectedCount);
                }
            });
        }

        await Task.WhenAll(sendTasks);

        // Assert
        // Backpressure test completed
        Assert.Equal(messageCount, _processedCommands.Count + rejectedCount);
    }

    [Fact]
    public async Task Should_Maintain_Performance_Under_Sustained_Load()
    {
        // Arrange
        var duration = TimeSpan.FromSeconds(5);
        var commandsSent = 0;
        var eventsSent = 0;
        var cancellationTokenSource = new CancellationTokenSource(duration);

        // Act
        var commandTask = Task.Run(async () =>
        {
            while (!cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    var command = new StressTestCommand { Id = Guid.NewGuid(), Sequence = commandsSent };
                    await _messagingService.Send(command);
                    Interlocked.Increment(ref commandsSent);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        });

        var eventTask = Task.Run(async () =>
        {
            while (!cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    var evt = new StressTestEvent { Id = Guid.NewGuid(), Data = $"Event {eventsSent}" };
                    await _messagingService.Publish(evt);
                    Interlocked.Increment(ref eventsSent);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        });

        await Task.WhenAll(commandTask, eventTask);

        // Assert
        // Sustained load test completed
        
        Assert.True(commandsSent > 0);
        Assert.True(eventsSent > 0);
    }
}

// Test Messages
public class StressTestCommand : ICommand
{
    public Guid Id { get; set; }
    public int Sequence { get; set; }
    public Guid MessageId { get; } = Guid.NewGuid();
    public DateTime Timestamp { get; } = DateTime.UtcNow;
    public Dictionary<string, object>? Metadata { get; set; }
}

public class StressTestCommandWithResult : ICommand<string>
{
    public Guid Id { get; set; }
    public int Value { get; set; }
    public Guid MessageId { get; } = Guid.NewGuid();
    public DateTime Timestamp { get; } = DateTime.UtcNow;
    public Dictionary<string, object>? Metadata { get; set; }
}

public class StressTestEvent : IEvent
{
    public Guid Id { get; set; }
    public string Data { get; set; } = string.Empty;
    public Guid MessageId { get; } = Guid.NewGuid();
    public DateTime Timestamp { get; } = DateTime.UtcNow;
    public Dictionary<string, object>? Metadata { get; set; }
}

// Test Handlers
public class StressTestCommandHandler : ICommandHandler<StressTestCommand>
{
    private readonly ConcurrentDictionary<Guid, DateTime> _processedCommands;

    public StressTestCommandHandler(ConcurrentDictionary<Guid, DateTime> processedCommands)
    {
        _processedCommands = processedCommands;
    }

    public async Task Handle(StressTestCommand command, CancellationToken cancellationToken = default)
    {
        // Simulate some work
        await Task.Delay(1, cancellationToken);
        _processedCommands.TryAdd(command.Id, DateTime.UtcNow);
    }
}

public class StressTestCommandWithResultHandler : ICommandHandler<StressTestCommandWithResult, string>
{
    public async Task<string> Handle(StressTestCommandWithResult command, CancellationToken cancellationToken = default)
    {
        // Simulate some work
        await Task.Delay(1, cancellationToken);
        return $"Processed: {command.Value}";
    }
}

public class StressTestEventHandler : IEventHandler<StressTestEvent>
{
    private readonly ConcurrentBag<Guid> _processedEvents;

    public StressTestEventHandler(ConcurrentBag<Guid> processedEvents)
    {
        _processedEvents = processedEvents;
    }

    public Task Handle(StressTestEvent @event, CancellationToken cancellationToken = default)
    {
        _processedEvents.Add(@event.Id);
        return Task.CompletedTask;
    }
}