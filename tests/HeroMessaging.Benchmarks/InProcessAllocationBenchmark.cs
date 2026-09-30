using System.Collections.Immutable;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Processing;
using HeroMessaging.Processing.Decorators;
using Microsoft.Extensions.Logging.Abstractions;

namespace HeroMessaging.Benchmarks;

internal static class InProcessAllocationBenchmark
{
    private const int Iterations = 100_000;
    private const int EventCount = 10_000;

    public static void Run()
    {
        var message = new InProcessEvent();
        var context = new ProcessingContext("EventBus", ImmutableDictionary<string, object>.Empty
            .Add("EventType", nameof(InProcessEvent)).Add("HandlerType", nameof(InProcessHandler)));
        var core = new NoopProcessor();
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        using var provider = services.BuildServiceProvider();
        var builder = new MessageProcessingPipelineBuilder(provider)
            .UseMetrics().UseLogging().UseCorrelation().UseValidation().UseErrorHandling().UseRetry();
        var pipeline = builder.Build(core);
        var logging = new LoggingDecorator(core, NullLogger<LoggingDecorator>.Instance, TimeProvider.System);
        var retry = new RetryDecorator(core, NullLogger<RetryDecorator>.Instance, TimeProvider.System);
        var correlation = new CorrelationContextDecorator(core, NullLogger<CorrelationContextDecorator>.Instance);
        object? retained = null;

        Console.WriteLine("Synchronous allocation attribution; no EventBus queue or async handler work.");
        Measure("Event object (B/event)", () => retained = new InProcessEvent(), Iterations);
        Measure("Pipeline build (B/delivery)", () => retained = builder.Build(core), Iterations);
        Measure("Core execution (B/delivery)", () => Process(core, message, context), Iterations);
        Measure("Logging disabled (B/delivery)", () => Process(logging, message, context), Iterations);
        Measure("Retry success (B/delivery)", () => Process(retry, message, context), Iterations);
        Measure("Correlation execution (B/delivery)", () => Process(correlation, message, context), Iterations);
        Measure("Default pipeline execution (B/delivery)", () => Process(pipeline, message, context), Iterations);

        var messageId = message.MessageId.ToString();
        Measure("Three metadata SetItem calls (B/delivery)", () => retained = context
            .WithMetadata("CorrelationId", messageId).WithMetadata("CausationId", string.Empty)
            .WithMetadata("MessageId", messageId).Metadata, Iterations);
        Measure("Metadata builder candidate (B/delivery)", () =>
        {
            var metadata = context.Metadata.ToBuilder();
            metadata["CorrelationId"] = messageId;
            metadata["CausationId"] = string.Empty;
            metadata["MessageId"] = messageId;
            retained = metadata.ToImmutable();
        }, Iterations);

        var sink = new InProcessSink();
        Measure("Sink arrays, three handlers (B/event)", () => sink.Start(EventCount, 3), 10, EventCount);
        sink.Start(EventCount, 3);
        for (var sequence = 0; sequence < EventCount; sequence++)
        {
            sink.RecordPublishing(sequence);
            sink.RecordAccepted(sequence);
            for (var handler = 0; handler < 3; handler++)
                sink.RecordHandlerCompleted(sequence, handler);
        }
        Measure("Latency result arrays (B/event)", () =>
        {
            var (accept, firstHandler, completion, _) = sink.GetLatencies();
            GC.KeepAlive(accept);
            GC.KeepAlive(firstHandler);
            GC.KeepAlive(completion);
        }, 10, EventCount);
        GC.KeepAlive(retained);
    }

    private static void Process(IMessageProcessor processor, IMessage message, ProcessingContext context)
    {
        var operation = processor.ProcessAsync(message, context);
        if (!operation.IsCompletedSuccessfully)
            throw new InvalidOperationException("Allocation attribution requires synchronous completion.");
        if (!operation.Result.Success)
            throw new InvalidOperationException("Allocation attribution requires successful processing.");
    }

    private static void Measure(string name, Action operation, int iterations, int unitsPerIteration = 1)
    {
        for (var warmup = 0; warmup < Math.Min(iterations, 1_000); warmup++)
            operation();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var iteration = 0; iteration < iterations; iteration++)
            operation();
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Console.WriteLine($"{name}: {bytes / ((double)iterations * unitsPerIteration):F2}");
    }

    private sealed class NoopProcessor : IMessageProcessor
    {
        public ValueTask<ProcessingResult> ProcessAsync(IMessage message, ProcessingContext context,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(ProcessingResult.Successful());
    }
}
