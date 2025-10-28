using BenchmarkDotNet.Running;

namespace HeroMessaging.Benchmarks;

public static class Program
{
    public static void Main(string[] args)
    {
        var summary = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
