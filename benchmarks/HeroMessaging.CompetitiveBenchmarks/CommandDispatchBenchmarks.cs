using BenchmarkDotNet.Attributes;

namespace HeroMessaging.CompetitiveBenchmarks;

[MemoryDiagnoser]
[BenchmarkCategory("InProcessCommandDispatch")]
public class CommandDispatchBenchmarks
{
    private ProviderHarness _hero = null!;
    private ProviderHarness _massTransit = null!;
    private readonly CompetitiveCommand _command = new();
    private long _heroAttempts;
    private long _massTransitAttempts;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _hero = ProviderHarness.Create("hero");
        _massTransit = ProviderHarness.Create("masstransit");
        await _hero.SendAsync(_command, CancellationToken.None).ConfigureAwait(false);
        await _massTransit.SendAsync(_command, CancellationToken.None).ConfigureAwait(false);
        _hero.Work.RequireCompleted(default, 1);
        _massTransit.Work.RequireCompleted(default, 1);
    }

    [Benchmark(Baseline = true, Description = "MassTransit 8.5.12 scoped mediator Send (handler complete)")]
    public Task MassTransitSend()
    {
        _massTransitAttempts++;
        return _massTransit.Mediator!.Send(_command, CancellationToken.None);
    }

    [Benchmark(Description = "HeroMessaging public SendAsync (handler complete)")]
    public Task HeroMessagingSend()
    {
        _heroAttempts++;
        return _hero.Hero!.SendAsync(_command, CancellationToken.None);
    }

    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        try
        {
            var afterSetup = new WorkSnapshot(1, BenchmarkWork.ExpectedPerCommandChecksum);
            _hero.Work.RequireCompleted(afterSetup, _heroAttempts);
            _massTransit.Work.RequireCompleted(afterSetup, _massTransitAttempts);
        }
        finally
        {
            await _hero.DisposeAsync().ConfigureAwait(false);
            await _massTransit.DisposeAsync().ConfigureAwait(false);
        }
    }
}
