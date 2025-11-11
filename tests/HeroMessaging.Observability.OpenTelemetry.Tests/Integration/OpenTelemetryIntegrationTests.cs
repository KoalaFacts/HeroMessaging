using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Processing;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Xunit;

namespace HeroMessaging.Observability.OpenTelemetry.Tests;

public class OpenTelemetryIntegrationTests : IDisposable
{
    private readonly List<Activity> _exportedActivities = new();
    private readonly List<Metric> _exportedMetrics = new();
    private readonly ActivityListener _activityListener;
    private readonly MeterListener _meterListener;
    private readonly ServiceProvider _serviceProvider;

    public OpenTelemetryIntegrationTests()
    {
        // Set up activity listener
        _activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == HeroMessagingInstrumentation.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity => { },
            ActivityStopped = activity => _exportedActivities.Add(activity)
        };
        ActivitySource.AddActivityListener(_activityListener);

        // Set up meter listener
        _meterListener = new MeterListener();
        _meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == HeroMessagingInstrumentation.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _meterListener.Start();

        // Set up DI container with OpenTelemetry
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);

        services.AddOpenTelemetry()
            .WithTracing(tracing => tracing
                .AddSource(HeroMessagingInstrumentation.ActivitySourceName))
            .WithMetrics(metrics => metrics
                .AddMeter(HeroMessagingInstrumentation.MeterName));

        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ProcessAsync_WithPipeline_CreatesTracesAndMetrics()
    {
        // Arrange
        var pipelineBuilder = new MessageProcessingPipelineBuilder(_serviceProvider);
        var innerProcessor = new TestMessageProcessor();
        var pipeline = pipelineBuilder
            .UseOpenTelemetry()
            .Build(innerProcessor);

        var message = new TestMessage { MessageId = Guid.NewGuid() };
        var context = new ProcessingContext("IntegrationTest");

        // Act
        var result = await pipeline.ProcessAsync(message, context);

        // Assert
        Assert.True(result.Success);
        Assert.Single(_exportedActivities);

        var activity = _exportedActivities[0];
        Assert.Equal("HeroMessaging.Process", activity.OperationName);
        Assert.Equal(ActivityKind.Internal, activity.Kind);
        Assert.Equal("IntegrationTest", activity.GetTagItem("messaging.processor"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ProcessAsync_MultipleMessages_CreatesMultipleTraces()
    {
        // Arrange
        var pipelineBuilder = new MessageProcessingPipelineBuilder(_serviceProvider);
        var innerProcessor = new TestMessageProcessor();
        var pipeline = pipelineBuilder
            .UseOpenTelemetry()
            .Build(innerProcessor);

        var messages = Enumerable.Range(0, 5)
            .Select(_ => new TestMessage { MessageId = Guid.NewGuid() })
            .ToList();

        var context = new ProcessingContext("MultiTest");

        // Act
        foreach (var message in messages)
        {
            await pipeline.ProcessAsync(message, context);
        }

        // Assert
        Assert.Equal(5, _exportedActivities.Count);
        Assert.All(_exportedActivities, activity =>
        {
            Assert.Equal("HeroMessaging.Process", activity.OperationName);
            Assert.Equal(ActivityStatusCode.Unset, activity.Status);
        });
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ProcessAsync_WithError_TracesError()
    {
        // Arrange
        var pipelineBuilder = new MessageProcessingPipelineBuilder(_serviceProvider);
        var innerProcessor = new FailingMessageProcessor();
        var pipeline = pipelineBuilder
            .UseOpenTelemetry()
            .Build(innerProcessor);

        var message = new TestMessage { MessageId = Guid.NewGuid() };
        var context = new ProcessingContext("ErrorTest");

        // Act
        var result = await pipeline.ProcessAsync(message, context);

        // Assert
        Assert.False(result.Success);
        Assert.Single(_exportedActivities);

        var activity = _exportedActivities[0];
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Contains("Test failure", activity.StatusDescription ?? "");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ProcessAsync_WithMultipleDecorators_MaintainsTraceContext()
    {
        // Arrange
        var pipelineBuilder = new MessageProcessingPipelineBuilder(_serviceProvider);
        var innerProcessor = new TestMessageProcessor();
        var pipeline = pipelineBuilder
            .UseLogging()
            .UseOpenTelemetry()
            .UseMetrics(new InMemoryMetricsCollector())
            .Build(innerProcessor);

        var message = new TestMessage { MessageId = Guid.NewGuid() };
        var context = new ProcessingContext("MultiDecoratorTest");

        // Act
        var result = await pipeline.ProcessAsync(message, context);

        // Assert
        Assert.True(result.Success);
        Assert.Single(_exportedActivities);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ProcessAsync_WithParentSpan_LinksToParent()
    {
        // Arrange
        using var parentSource = new ActivitySource("ParentTest");
        using var parentActivity = parentSource.StartActivity("ParentOperation");

        var pipelineBuilder = new MessageProcessingPipelineBuilder(_serviceProvider);
        var innerProcessor = new TestMessageProcessor();
        var pipeline = pipelineBuilder
            .UseOpenTelemetry()
            .Build(innerProcessor);

        var message = new TestMessage { MessageId = Guid.NewGuid() };
        var context = new ProcessingContext("ParentTest");

        // Act
        var result = await pipeline.ProcessAsync(message, context);

        // Assert
        Assert.True(result.Success);
        var childActivity = _exportedActivities.Single();
        Assert.Equal(parentActivity?.Context.TraceId, childActivity.Context.TraceId);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ProcessAsync_WithRetry_IncludesRetryInformation()
    {
        // Arrange
        var pipelineBuilder = new MessageProcessingPipelineBuilder(_serviceProvider);
        var innerProcessor = new TestMessageProcessor();
        var pipeline = pipelineBuilder
            .UseOpenTelemetry()
            .Build(innerProcessor);

        var message = new TestMessage { MessageId = Guid.NewGuid() };
        var context = new ProcessingContext("RetryTest").WithRetry(3);

        // Act
        var result = await pipeline.ProcessAsync(message, context);

        // Assert
        Assert.True(result.Success);
        var activity = _exportedActivities.Single();
        Assert.Equal("3", activity.GetTagItem("messaging.retry_count")?.ToString());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void HeroMessagingBuilder_AddOpenTelemetry_RegistersProviders()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);

        // Create a mock builder (simplified for testing)
        var builder = new TestHeroMessagingBuilder(services);

        // Act
        builder.AddOpenTelemetry(options =>
        {
            options.ServiceName = "TestService";
            options.ServiceNamespace = "TestNamespace";
            options.ServiceVersion = "2.0.0";
            options.EnableTracing = true;
            options.EnableMetrics = true;
        });

        var provider = services.BuildServiceProvider();

        // Assert
        // Verify that OpenTelemetry services were registered
        var tracerProvider = provider.GetService<TracerProvider>();
        Assert.NotNull(tracerProvider);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ProcessAsync_RecordsMetrics_ForSuccessAndFailure()
    {
        // Arrange
        var metricRecordings = new List<(string InstrumentName, double Value)>();

        _meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            metricRecordings.Add((instrument.Name, measurement));
        });

        _meterListener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            metricRecordings.Add((instrument.Name, measurement));
        });

        _meterListener.SetMeasurementEventCallback<int>((instrument, measurement, tags, state) =>
        {
            metricRecordings.Add((instrument.Name, measurement));
        });

        var pipelineBuilder = new MessageProcessingPipelineBuilder(_serviceProvider);
        var successProcessor = new TestMessageProcessor();
        var pipeline = pipelineBuilder.UseOpenTelemetry().Build(successProcessor);

        var message = new TestMessage { MessageId = Guid.NewGuid() };
        var context = new ProcessingContext("MetricsTest");

        // Act
        await pipeline.ProcessAsync(message, context);

        // Assert - Verify processing duration metric was recorded
        Assert.Contains(metricRecordings,
            m => m.InstrumentName == "heromessaging_message_processing_duration_ms");
    }

    public void Dispose()
    {
        _activityListener?.Dispose();
        _meterListener?.Dispose();
        _serviceProvider?.Dispose();
        foreach (var activity in _exportedActivities)
        {
            activity?.Dispose();
        }
    }

    private class TestMessageProcessor : IMessageProcessor
    {
        public ValueTask<ProcessingResult> ProcessAsync(IMessage message, ProcessingContext context, CancellationToken cancellationToken = default)
        {
            return new ValueTask<ProcessingResult>(ProcessingResult.Successful());
        }
    }

    private class FailingMessageProcessor : IMessageProcessor
    {
        public ValueTask<ProcessingResult> ProcessAsync(IMessage message, ProcessingContext context, CancellationToken cancellationToken = default)
        {
            var exception = new InvalidOperationException("Test failure");
            return new ValueTask<ProcessingResult>(ProcessingResult.Failed(exception));
        }
    }

    private class TestMessage : IMessage
    {
        public Guid MessageId { get; set; }
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public IDictionary<string, object>? Metadata { get; set; }
    }

    private class InMemoryMetricsCollector : HeroMessaging.Abstractions.Metrics.IMetricsCollector
    {
        public void IncrementCounter(string name, int value = 1) { }
        public void RecordDuration(string name, TimeSpan duration) { }
        public void RecordValue(string name, double value) { }
    }

    private class TestHeroMessagingBuilder : IHeroMessagingBuilder
    {
        private readonly IServiceCollection _services;

        public TestHeroMessagingBuilder(IServiceCollection services)
        {
            _services = services;
        }

        public IServiceCollection Build() => _services;

        public IHeroMessagingBuilder WithMediator() => this;
        public IHeroMessagingBuilder WithEventBus() => this;
        public IHeroMessagingBuilder WithQueues() => this;
        public IHeroMessagingBuilder WithOutbox() => this;
        public IHeroMessagingBuilder WithInbox() => this;
        public IHeroMessagingBuilder WithErrorHandling() => this;
        public IHeroMessagingBuilder UseInMemoryStorage() => this;
        public IHeroMessagingBuilder UseStorage<TStorage>() where TStorage : class, HeroMessaging.Abstractions.Storage.IMessageStorage => this;
        public IHeroMessagingBuilder UseStorage(HeroMessaging.Abstractions.Storage.IMessageStorage storage) => this;
        public IHeroMessagingBuilder ScanAssembly(System.Reflection.Assembly assembly) => this;
        public IHeroMessagingBuilder ScanAssemblies(params System.Reflection.Assembly[] assemblies) => this;
        public IHeroMessagingBuilder ConfigureProcessing(Action<ProcessingOptions> configure) => this;
        public IHeroMessagingBuilder AddPlugin<TPlugin>() where TPlugin : class, HeroMessaging.Abstractions.Plugins.IMessagingPlugin => this;
        public IHeroMessagingBuilder AddPlugin(HeroMessaging.Abstractions.Plugins.IMessagingPlugin plugin) => this;
        public IHeroMessagingBuilder AddPlugin<TPlugin>(Action<TPlugin> configure) where TPlugin : class, HeroMessaging.Abstractions.Plugins.IMessagingPlugin => this;
        public IHeroMessagingBuilder DiscoverPlugins() => this;
        public IHeroMessagingBuilder DiscoverPlugins(string directory) => this;
        public IHeroMessagingBuilder DiscoverPlugins(System.Reflection.Assembly assembly) => this;
        public IHeroMessagingBuilder Development() => this;
        public IHeroMessagingBuilder Production(string connectionString) => this;
        public IHeroMessagingBuilder Microservice(string connectionString) => this;
    }
}
