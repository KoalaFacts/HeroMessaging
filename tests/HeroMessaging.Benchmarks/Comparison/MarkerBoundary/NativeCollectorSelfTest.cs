namespace HeroMessaging.Benchmarks;

internal static class NativeCollectorSelfTest
{
    internal static void Run()
    {
        var collector = new NativeCollector();
        Require(collector.OnOutput(3, IntPtr.Zero, 0) == 0, "Progress must not stop collection.");
        Require(collector.OnOutput(0, IntPtr.Zero, 0) == 0, "Empty normal output must not stop collection.");
        collector.Stop();
        Require(collector.OnOutput(3, IntPtr.Zero, 0) == 1, "Duration cancellation must reach the callback.");
        Require(new NativeCollector().OnOutput(2, IntPtr.Zero, 0) == 1, "Native errors must stop collection even without text.");
        Require(new NativeCollector().OnOutput(0, IntPtr.Zero, 1) == 1, "Invalid callback data must fail closed.");
        Require(new NativeCollector().OnOutput(0, IntPtr.Zero, (nuint)int.MaxValue + 1) == 1, "Oversized callback data must fail closed.");
        var timed = new NativeCollector();
        using var timer = new Timer(_ => timed.Stop(), null, 10, Timeout.Infinite);
        Require(SpinWait.SpinUntil(() => timed.OnOutput(3, IntPtr.Zero, 0) == 1, TimeSpan.FromSeconds(2)),
            "The managed timer must terminate collection through the callback.");
        Console.WriteLine("Native adapter callback self-test passed; no native library loaded.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
