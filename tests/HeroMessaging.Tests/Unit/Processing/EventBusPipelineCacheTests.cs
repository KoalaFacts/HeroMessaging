using System.Collections.Concurrent;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Validation;
using HeroMessaging.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroMessaging.Tests.Unit.Processing;

public sealed class EventBusPipelineCacheTests
{
    [Fact]
    public async Task CachedPipelineUsesEachTransientHandlerInstance()
    {
        var sink = new HandlerSink();
        var nextId = 0;
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddTransient<IEventHandler<DispatchEvent>>(_ =>
            new TrackingHandler(Interlocked.Increment(ref nextId), sink));
        using var provider = services.BuildServiceProvider();
        await using var bus = new EventBus(provider, NullLogger<EventBus>.Instance);

        await bus.PublishAsync(new DispatchEvent(), TestContext.Current.CancellationToken);
        await bus.PublishAsync(new DispatchEvent(), TestContext.Current.CancellationToken);
        await sink.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(2, sink.Instances.Count);
        Assert.Equal(2, sink.Instances.Distinct().Count());
    }

    [Fact]
    public async Task TransientValidatorIsResolvedForEachEvent()
    {
        var sink = new HandlerSink();
        var validatorIds = new ConcurrentBag<int>();
        var nextValidatorId = 0;
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IEventHandler<DispatchEvent>>(new TrackingHandler(1, sink));
        services.AddTransient<IMessageValidator>(_ =>
            new TrackingValidator(Interlocked.Increment(ref nextValidatorId), validatorIds));
        using var provider = services.BuildServiceProvider();
        await using var bus = new EventBus(provider, NullLogger<EventBus>.Instance);

        await bus.PublishAsync(new DispatchEvent(), TestContext.Current.CancellationToken);
        await bus.PublishAsync(new DispatchEvent(), TestContext.Current.CancellationToken);
        await sink.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(2, validatorIds.Distinct().Count());
    }

    private sealed class HandlerSink
    {
        private int _count;
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ConcurrentBag<int> Instances { get; } = [];
        public Task Completion => _completion.Task;

        public void Record(int id)
        {
            Instances.Add(id);
            if (Interlocked.Increment(ref _count) == 2)
                _completion.TrySetResult();
        }
    }

    private sealed class TrackingHandler(int id, HandlerSink sink) : IEventHandler<DispatchEvent>
    {
        public Task HandleAsync(DispatchEvent message, CancellationToken cancellationToken = default)
        {
            sink.Record(id);
            return Task.CompletedTask;
        }
    }

    private sealed class TrackingValidator(int id, ConcurrentBag<int> instances) : IMessageValidator
    {
        public ValueTask<ValidationResult> ValidateAsync(IMessage message, CancellationToken cancellationToken = default)
        {
            instances.Add(id);
            return ValueTask.FromResult(ValidationResult.Success());
        }
    }

    private sealed class DispatchEvent : IEvent
    {
        public Guid MessageId { get; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }
}
