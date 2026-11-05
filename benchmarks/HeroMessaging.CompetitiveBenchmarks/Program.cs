using System.Text.Json;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

namespace HeroMessaging.CompetitiveBenchmarks;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0] == "--verify")
            {
                var negativeControl = args.Length switch
                {
                    1 => "none",
                    3 when args[1] == "--negative-control" => args[2],
                    _ => throw new ArgumentException("Use --verify [--negative-control missing-work|early-completion|missing-handler].")
                };
                var report = await Verification.RunAsync(negativeControl).ConfigureAwait(false);
                Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
                return report.Verified ? 0 : 1;
            }

            if (args.Length > 0 && args[0] == "--resource")
            {
                var report = await ResourceMeasurements.RunAsync(ResourceOptions.Parse(args)).ConfigureAwait(false);
                Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
                return 0;
            }

            var config = ManualConfig.Create(DefaultConfig.Instance).AddValidator(new OutOfProcessValidator());
            var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config).ToArray();
            return summaries.Length > 0 && summaries.All(summary => !summary.HasCriticalValidationErrors
                && summary.Reports.All(report => report.Success)) ? 0 : 1;
        }
        catch (ArgumentException exception)
        {
            return ReportFailure(exception);
        }
        catch (InvalidOperationException exception)
        {
            return ReportFailure(exception);
        }
        catch (OperationCanceledException exception)
        {
            return ReportFailure(exception);
        }
        catch (IOException exception)
        {
            return ReportFailure(exception);
        }
    }

    private static int ReportFailure(Exception exception)
    {
        Console.Error.WriteLine($"Competitive benchmark failed: {exception}");
        return 1;
    }
}
