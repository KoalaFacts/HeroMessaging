namespace HeroMessaging.CompetitiveBenchmarks;

internal sealed record VerificationResult(string Provider, bool Verified, string Detail);
internal sealed record VerificationReport(bool Verified, string NegativeControl, VerificationResult[] Results);

internal static class Verification
{
    public static async Task<VerificationReport> RunAsync(string negativeControl)
    {
        if (negativeControl is not ("none" or "missing-work" or "early-completion" or "missing-handler"))
            throw new ArgumentException("Unknown verification negative control.", nameof(negativeControl));

        var results = new List<VerificationResult>();
        foreach (var name in new[] { "hero", "masstransit" })
        {
            try
            {
                await VerifyProviderAsync(name, negativeControl).ConfigureAwait(false);
                results.Add(new VerificationResult(name, true, "Count/checksum and gated asynchronous handler completion verified."));
            }
            catch (Exception exception)
            {
                results.Add(new VerificationResult(name, false, $"{exception.GetType().Name}: {exception.Message}"));
            }
        }

        return new VerificationReport(results.All(result => result.Verified), negativeControl, [.. results]);
    }

    private static async Task VerifyProviderAsync(string name, string negativeControl)
    {
        await using var harness = ProviderHarness.Create(name);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        if (negativeControl == "missing-handler")
            await harness.SendUnhandledAsync(timeout.Token).ConfigureAwait(false);

        var command = new CompetitiveCommand();
        var before = harness.Work.Snapshot;
        for (var index = 0; index < 4; index++)
        {
            if (negativeControl != "missing-work")
                await harness.SendAsync(command, timeout.Token).ConfigureAwait(false);
        }

        harness.Work.RequireCompleted(before, 4);
        await VerifyGatedCompletionAsync(harness, negativeControl == "early-completion", timeout.Token).ConfigureAwait(false);
    }

    private static async Task VerifyGatedCompletionAsync(ProviderHarness harness, bool returnEarly, CancellationToken cancellationToken)
    {
        var gate = new CompletionGate();
        var before = harness.Work.Snapshot;
        var actualDispatch = harness.SendAsync(new CompetitiveCommand { Gate = gate }, cancellationToken);
        var observedDispatch = returnEarly ? Task.CompletedTask : actualDispatch;
        try
        {
            await gate.Started.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (observedDispatch.IsCompleted || harness.Work.Snapshot != before)
                throw new InvalidOperationException("Dispatch completed before the gated handler completed its work.");
        }
        finally
        {
            gate.Released.TrySetResult();
            await actualDispatch.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        await observedDispatch.ConfigureAwait(false);
        harness.Work.RequireCompleted(before, 1);
    }
}
