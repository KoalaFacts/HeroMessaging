using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HeroMessaging.Observability.HealthChecks;

/// <summary>
/// Composite health check that aggregates multiple named health checks into a single logical check.
/// </summary>
/// <remarks>
/// This health check provides a way to group related health checks under a single name,
/// making it easier to organize and report on logical system components.
///
/// Note: This is a basic implementation that always reports Healthy with metadata about
/// the constituent checks. A full implementation would need to query the actual health check
/// results from the health check service.
///
/// Example usage:
/// <code>
/// services.AddHealthChecks()
///     .AddCheck("database", databaseHealthCheck)
///     .AddCheck("cache", cacheHealthCheck)
///     .AddCompositeHealthCheck("storage", "database", "cache");
/// </code>
/// </remarks>
public class CompositeHealthCheck(params string[] checkNames) : IHealthCheck
{
    private readonly string[] _checkNames = checkNames ?? throw new ArgumentNullException(nameof(checkNames));

    /// <summary>
    /// Checks the health of the composite group by referencing the constituent health check names.
    /// </summary>
    /// <param name="context">The health check context provided by the health check system</param>
    /// <param name="cancellationToken">Cancellation token to abort the health check operation</param>
    /// <returns>A task containing the health check result with information about the constituent checks</returns>
    /// <remarks>
    /// Currently returns Healthy with metadata listing the check names and count.
    /// A complete implementation would aggregate actual results from the referenced checks.
    /// </remarks>
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