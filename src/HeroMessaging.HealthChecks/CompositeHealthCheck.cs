using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Diagnostics;

namespace HeroMessaging.HealthChecks;

public class HeroMessagingHealthCheck : IHealthCheck
{
    private readonly IEnumerable<IHealthCheck> _healthChecks;
    private readonly string _name;

    public HeroMessagingHealthCheck(
        IEnumerable<IHealthCheck> healthChecks,
        string name = "hero_messaging")
    {
        _healthChecks = healthChecks ?? throw new ArgumentNullException(nameof(healthChecks));
        _name = name;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var results = new Dictionary<string, HealthCheckResult>();
        var tasks = new List<Task<(string, HealthCheckResult)>>();

        foreach (var healthCheck in _healthChecks)
        {
            tasks.Add(CheckComponentAsync(healthCheck, context, cancellationToken));
        }

        var completedTasks = await Task.WhenAll(tasks);
        
        foreach (var (name, result) in completedTasks)
        {
            results[name] = result;
        }

        stopwatch.Stop();

        var unhealthyCount = results.Count(r => r.Value.Status == HealthStatus.Unhealthy);
        var degradedCount = results.Count(r => r.Value.Status == HealthStatus.Degraded);
        var healthyCount = results.Count(r => r.Value.Status == HealthStatus.Healthy);

        var data = new Dictionary<string, object>
        {
            ["total_checks"] = results.Count,
            ["healthy"] = healthyCount,
            ["degraded"] = degradedCount,
            ["unhealthy"] = unhealthyCount,
            ["duration_ms"] = stopwatch.ElapsedMilliseconds,
            ["components"] = results.ToDictionary(
                r => r.Key,
                r => new
                {
                    status = r.Value.Status.ToString(),
                    description = r.Value.Description,
                    data = r.Value.Data
                })
        };

        if (unhealthyCount > 0)
        {
            return HealthCheckResult.Unhealthy(
                $"{_name}: {unhealthyCount} component(s) unhealthy",
                data: data);
        }

        if (degradedCount > 0)
        {
            return HealthCheckResult.Degraded(
                $"{_name}: {degradedCount} component(s) degraded",
                data: data);
        }

        return HealthCheckResult.Healthy(
            $"{_name}: All components healthy",
            data);
    }

    private async Task<(string, HealthCheckResult)> CheckComponentAsync(
        IHealthCheck healthCheck,
        HealthCheckContext context,
        CancellationToken cancellationToken)
    {
        var name = healthCheck.GetType().Name.Replace("HealthCheck", "");
        
        try
        {
            var result = await healthCheck.CheckHealthAsync(context, cancellationToken);
            return (name, result);
        }
        catch (Exception ex)
        {
            return (name, HealthCheckResult.Unhealthy($"Check failed: {ex.Message}", ex));
        }
    }
}

public class ReadinessCheck : IHealthCheck
{
    private readonly HeroMessagingHealthCheck _compositeCheck;
    
    public ReadinessCheck(HeroMessagingHealthCheck compositeCheck)
    {
        _compositeCheck = compositeCheck ?? throw new ArgumentNullException(nameof(compositeCheck));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var result = await _compositeCheck.CheckHealthAsync(context, cancellationToken);
        
        if (result.Status == HealthStatus.Unhealthy)
        {
            return HealthCheckResult.Unhealthy(
                "System not ready to accept traffic",
                data: result.Data);
        }

        return HealthCheckResult.Healthy(
            "System ready to accept traffic",
            result.Data);
    }
}

public class LivenessCheck : IHealthCheck
{
    private readonly IEnumerable<IHealthCheck> _criticalChecks;
    
    public LivenessCheck(IEnumerable<IHealthCheck> criticalChecks)
    {
        _criticalChecks = criticalChecks ?? throw new ArgumentNullException(nameof(criticalChecks));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        foreach (var check in _criticalChecks)
        {
            try
            {
                var result = await check.CheckHealthAsync(context, cancellationToken);
                if (result.Status == HealthStatus.Unhealthy)
                {
                    return HealthCheckResult.Unhealthy(
                        "Critical component failure detected",
                        data: new Dictionary<string, object>
                        {
                            ["failed_component"] = check.GetType().Name,
                            ["details"] = result.Description
                        });
                }
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy(
                    "Liveness check failed",
                    ex);
            }
        }

        return HealthCheckResult.Healthy("System is alive");
    }
}