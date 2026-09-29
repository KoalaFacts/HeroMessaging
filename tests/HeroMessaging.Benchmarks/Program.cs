using BenchmarkDotNet.Running;

namespace HeroMessaging.Benchmarks;

public static class Program
{
    public static async Task Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--pipeline")
        {
            await PostgreSqlRabbitMqPipelineBenchmark.RunAsync(args[1..]);
            return;
        }

        _ = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
