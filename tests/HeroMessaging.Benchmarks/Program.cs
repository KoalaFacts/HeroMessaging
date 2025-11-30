using BenchmarkDotNet.Running;

namespace HeroMessaging.Benchmarks;

public static class Program
{
    public static void Main(string[] args)
    {
        _ = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
