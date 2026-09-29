using BenchmarkDotNet.Running;

namespace HeroMessaging.Benchmarks;

public static class Program
{
    public static async Task Main(string[] args)
    {
        if (args.Length > 0 && (args[0] is "--pipeline" or "--pipeline-direct"))
        {
            await PostgreSqlRabbitMqPipelineBenchmark.RunAsync(args[1..], direct: args[0] == "--pipeline-direct");
            return;
        }

        _ = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
