using BenchmarkDotNet.Validators;

namespace HeroMessaging.CompetitiveBenchmarks;

internal sealed class OutOfProcessValidator : IValidator
{
    public bool TreatsWarningsAsErrors => true;

    public IEnumerable<ValidationError> Validate(ValidationParameters validationParameters)
    {
        foreach (var benchmark in validationParameters.Benchmarks)
        {
            if (benchmark.Job.Infrastructure.TryGetToolchain(out var toolchain) && toolchain.IsInProcess)
                yield return new ValidationError(true,
                    "In-process toolchains are unsupported because cleanup failures may not produce a failing report. Use the default out-of-process toolchain.", benchmark);
        }
    }
}
