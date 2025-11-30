using Microsoft.Extensions.Diagnostics.HealthChecks;
using System;
using System.Linq;

namespace HeroMessaging.Observability.HealthChecks;

public class CompositeHealthCheck(params IEnumerable<string> checkNames) : IHealthCheck
{
    private readonly string[] _checkNames = checkNames?.ToArray() ?? throw new ArgumentNullException(nameof(checkNames));

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // In a real implementation, this would aggregate results from multiple health checks
        // For now, just return healthy
        var data = new Dictionary<string, object>
        {
            ["checks"] = _checkNames,
            ["check_count"] = _checkNames.Length
        };

        return Task.FromResult(HealthCheckResult.Healthy(
            $"Composite check for {_checkNames.Length} components",
            data));
    }
}
