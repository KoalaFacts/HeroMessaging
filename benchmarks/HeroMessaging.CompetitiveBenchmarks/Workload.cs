using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Handlers;
using MassTransit;

namespace HeroMessaging.CompetitiveBenchmarks;

public abstract record BenchmarkCommand : ICommand
{
    public Guid MessageId { get; init; } = new("d94b1be6-2689-4fb6-b9e0-14bb05de98b0");
    public DateTimeOffset Timestamp { get; init; } = new(2026, 10, 24, 0, 0, 0, TimeSpan.Zero);
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}

public sealed record CompetitiveCommand : BenchmarkCommand
{
    public long Seed { get; init; } = 7;
    public CompletionGate? Gate { get; init; }
}

public sealed record UnhandledCommand : BenchmarkCommand;

public sealed class CompletionGate
{
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public readonly record struct WorkSnapshot(long Completed, long Checksum);

public sealed class BenchmarkWork
{
    // Independently calculated for seed 7 and sixteen multiply/add steps.
    public const long ExpectedPerCommandChecksum = -7003726383735274993;
    private long _completed;
    private long _checksum;

    public WorkSnapshot Snapshot => new(_completed, _checksum);

    public Task ExecuteAsync(CompetitiveCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (command.Gate is { } gate)
            return ExecuteGatedAsync(command, gate, cancellationToken);

        Execute(command);
        return Task.CompletedTask;
    }

    private async Task ExecuteGatedAsync(CompetitiveCommand command, CompletionGate gate, CancellationToken cancellationToken)
    {
        gate.Started.TrySetResult();
        await gate.Released.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        Execute(command);
    }

    private void Execute(CompetitiveCommand command)
    {
        var value = command.Seed;
        for (var index = 0; index < 16; index++)
            value = unchecked(value * 31 + index);

        _checksum = unchecked(_checksum + value);
        _completed++;
    }

    public WorkSnapshot RequireCompleted(WorkSnapshot before, long expectedCount)
    {
        var actual = new WorkSnapshot(_completed - before.Completed, unchecked(_checksum - before.Checksum));
        if (actual.Completed != expectedCount || actual.Checksum != unchecked(ExpectedPerCommandChecksum * expectedCount))
            throw new InvalidOperationException($"Handler work incomplete: expected {expectedCount} commands with the expected checksum, observed {actual.Completed} commands and checksum {actual.Checksum}.");

        return actual;
    }
}

public sealed class HeroCommandHandler(BenchmarkWork work) : ICommandHandler<CompetitiveCommand>
{
    public Task HandleAsync(CompetitiveCommand command, CancellationToken cancellationToken) =>
        work.ExecuteAsync(command, cancellationToken);
}

public sealed class MassTransitCommandConsumer(BenchmarkWork work) : IConsumer<CompetitiveCommand>
{
    public Task Consume(ConsumeContext<CompetitiveCommand> context) =>
        work.ExecuteAsync(context.Message, context.CancellationToken);
}
