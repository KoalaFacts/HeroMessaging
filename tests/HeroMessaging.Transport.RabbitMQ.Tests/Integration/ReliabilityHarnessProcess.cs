using System.Diagnostics;
using Xunit;

namespace HeroMessaging.Transport.RabbitMQ.Tests.Integration;

/// <summary>HeroMessaging.ReliabilityHarness run as its own OS process, driven over stdin/stdout.</summary>
internal sealed class ReliabilityHarnessProcess : IDisposable
{
    private readonly Process _process;

    private ReliabilityHarnessProcess(Process process)
    {
        _process = process;
    }

    public int Id => _process.Id;

    public static ReliabilityHarnessProcess Start(IEnumerable<string> arguments)
    {
        var harness = Path.Join(AppContext.BaseDirectory, "HeroMessaging.ReliabilityHarness.dll");
        Assert.True(File.Exists(harness), $"Reliability harness not found at {harness}.");
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host ? host : "dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = false,
            UseShellExecute = false
        };
        start.ArgumentList.Add(harness);
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        return new ReliabilityHarnessProcess(Process.Start(start) ?? throw new InvalidOperationException("Could not start the reliability harness."));
    }

    public async Task<string> ReadLineAsync(TimeSpan limit, CancellationToken cancellationToken)
    {
        var line = await _process.StandardOutput.ReadLineAsync(cancellationToken).AsTask().WaitAsync(limit, cancellationToken);
        return line ?? throw new InvalidOperationException($"Reliability harness exited with {(_process.WaitForExit(5000) ? _process.ExitCode : -1)} before the expected output.");
    }

    public void Kill()
    {
        _process.Kill(entireProcessTree: true);
        _process.WaitForExit();
    }

    public async Task<int> StopAsync(TimeSpan limit, CancellationToken cancellationToken)
    {
        await _process.StandardInput.WriteLineAsync("stop");
        await _process.StandardInput.FlushAsync(cancellationToken);
        await _process.WaitForExitAsync(cancellationToken).WaitAsync(limit, cancellationToken);
        return _process.ExitCode;
    }

    public void Dispose()
    {
        if (!_process.HasExited)
            _process.Kill(entireProcessTree: true);
        _process.Dispose();
    }
}
