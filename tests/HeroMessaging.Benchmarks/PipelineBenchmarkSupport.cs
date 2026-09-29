using System.Diagnostics;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Handlers;

namespace HeroMessaging.Benchmarks;

public sealed class PipelineEvent : IEvent
{
    public Guid MessageId { get; set; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string? CorrelationId { get; set; }
    public string? CausationId { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
    public int Sequence { get; set; }
    public Guid RunId { get; set; }
}

public sealed class PipelineHandler(PipelineSink sink) : IEventHandler<PipelineEvent>
{
    public Task HandleAsync(PipelineEvent message, CancellationToken cancellationToken = default)
    {
        sink.RecordHandled(message.RunId, message.Sequence);
        return Task.CompletedTask;
    }
}

public sealed class PipelineSink
{
    private long[] _published = [];
    private long[] _handled = [];
    private int _completed;
    private int _duplicates;
    private TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Completion => _completion.Task;
    public Guid RunId { get; private set; }

    public void Start(int count)
    {
        _published = new long[count];
        _handled = new long[count];
        _completed = 0;
        _duplicates = 0;
        RunId = Guid.NewGuid();
        _completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public void RecordPublished(int sequence) => _published[sequence] = Stopwatch.GetTimestamp();

    public void RecordHandled(Guid runId, int sequence)
    {
        if (runId != RunId || sequence < 0 || sequence >= _handled.Length)
        {
            Interlocked.Increment(ref _duplicates);
            return;
        }

        if (Interlocked.CompareExchange(ref _handled[sequence], Stopwatch.GetTimestamp(), 0) != 0)
        {
            Interlocked.Increment(ref _duplicates);
            return;
        }

        if (Interlocked.Increment(ref _completed) == _handled.Length)
            _completion.TrySetResult();
    }

    public void Validate()
    {
        if (Volatile.Read(ref _duplicates) != 0)
            throw new InvalidOperationException("The handler ran more than once for a benchmark event.");
    }

    public double[] GetLatencies()
    {
        var latencies = new double[_published.Length];
        for (var index = 0; index < latencies.Length; index++)
            latencies[index] = Stopwatch.GetElapsedTime(_published[index], _handled[index]).TotalMilliseconds;
        return latencies;
    }
}
