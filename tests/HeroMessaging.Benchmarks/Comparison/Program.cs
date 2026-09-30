namespace HeroMessaging.Benchmarks;

internal static class Program
{
    private static Task Main(string[] args) => ConcurrentInProcessPipelineBenchmark.RunAsync(args);
}
