using Microsoft.Extensions.Diagnostics.HealthChecks;
using HeroMessaging.Core.Processing;
using HeroMessaging.Abstractions.Processing;
using System.Diagnostics;

namespace HeroMessaging.HealthChecks;

public class CommandProcessorHealthCheck : IHealthCheck
{
    private readonly CommandProcessor _processor;
    private readonly string _name;

    public CommandProcessorHealthCheck(CommandProcessor processor, string name = "command_processor")
    {
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _name = name;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var metrics = _processor.GetMetrics();
            
            var data = new Dictionary<string, object>
            {
                ["processed_count"] = metrics.ProcessedCount,
                ["failed_count"] = metrics.FailedCount,
                ["average_duration_ms"] = metrics.AverageDuration.TotalMilliseconds,
                ["is_running"] = _processor.IsRunning
            };

            if (!_processor.IsRunning)
            {
                return HealthCheckResult.Unhealthy($"{_name}: Processor is not running", data: data);
            }

            if (metrics.FailedCount > metrics.ProcessedCount * 0.1) 
            {
                return HealthCheckResult.Degraded(
                    $"{_name}: High failure rate detected",
                    data: data);
            }

            return HealthCheckResult.Healthy($"{_name}: Processor is operational", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                $"{_name}: Health check failed",
                ex,
                new Dictionary<string, object>
                {
                    ["error"] = ex.Message
                });
        }
    }
}

public class EventBusHealthCheck : IHealthCheck
{
    private readonly EventBus _eventBus;
    private readonly string _name;

    public EventBusHealthCheck(EventBus eventBus, string name = "event_bus")
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _name = name;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var metrics = _eventBus.GetMetrics();
            
            var data = new Dictionary<string, object>
            {
                ["published_count"] = metrics.PublishedCount,
                ["failed_count"] = metrics.FailedCount,
                ["handler_count"] = metrics.RegisteredHandlers,
                ["is_running"] = _eventBus.IsRunning
            };

            if (!_eventBus.IsRunning)
            {
                return HealthCheckResult.Unhealthy($"{_name}: Event bus is not running", data: data);
            }

            if (metrics.FailedCount > metrics.PublishedCount * 0.1)
            {
                return HealthCheckResult.Degraded(
                    $"{_name}: High failure rate detected",
                    data: data);
            }

            return HealthCheckResult.Healthy($"{_name}: Event bus is operational", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                $"{_name}: Health check failed",
                ex,
                new Dictionary<string, object>
                {
                    ["error"] = ex.Message
                });
        }
    }
}

public class QueryProcessorHealthCheck : IHealthCheck
{
    private readonly QueryProcessor _processor;
    private readonly string _name;

    public QueryProcessorHealthCheck(QueryProcessor processor, string name = "query_processor")
    {
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _name = name;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var metrics = _processor.GetMetrics();
            
            var data = new Dictionary<string, object>
            {
                ["processed_count"] = metrics.ProcessedCount,
                ["failed_count"] = metrics.FailedCount,
                ["average_duration_ms"] = metrics.AverageDuration.TotalMilliseconds,
                ["cache_hit_rate"] = metrics.CacheHitRate,
                ["is_running"] = _processor.IsRunning
            };

            if (!_processor.IsRunning)
            {
                return HealthCheckResult.Unhealthy($"{_name}: Processor is not running", data: data);
            }

            if (metrics.AverageDuration.TotalSeconds > 5)
            {
                return HealthCheckResult.Degraded(
                    $"{_name}: Slow query performance detected",
                    data: data);
            }

            return HealthCheckResult.Healthy($"{_name}: Processor is operational", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                $"{_name}: Health check failed",
                ex,
                new Dictionary<string, object>
                {
                    ["error"] = ex.Message
                });
        }
    }
}

public class QueueProcessorHealthCheck : IHealthCheck
{
    private readonly IQueueProcessor _processor;
    private readonly string _name;

    public QueueProcessorHealthCheck(IQueueProcessor processor, string name = "queue_processor")
    {
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _name = name;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var metrics = _processor.GetMetrics();
            var queues = await _processor.GetActiveQueues(cancellationToken);
            
            var data = new Dictionary<string, object>
            {
                ["active_queues"] = queues.Count(),
                ["total_messages"] = metrics.TotalMessages,
                ["processed_messages"] = metrics.ProcessedMessages,
                ["failed_messages"] = metrics.FailedMessages,
                ["is_running"] = _processor.IsRunning
            };

            if (!_processor.IsRunning)
            {
                return HealthCheckResult.Unhealthy($"{_name}: Processor is not running", data: data);
            }

            if (metrics.TotalMessages - metrics.ProcessedMessages > 10000)
            {
                return HealthCheckResult.Degraded(
                    $"{_name}: Large message backlog detected",
                    data: data);
            }

            return HealthCheckResult.Healthy($"{_name}: Processor is operational", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                $"{_name}: Health check failed",
                ex,
                new Dictionary<string, object>
                {
                    ["error"] = ex.Message
                });
        }
    }
}

public class OutboxProcessorHealthCheck : IHealthCheck
{
    private readonly IOutboxProcessor _processor;
    private readonly string _name;

    public OutboxProcessorHealthCheck(IOutboxProcessor processor, string name = "outbox_processor")
    {
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _name = name;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var metrics = _processor.GetMetrics();
            
            var data = new Dictionary<string, object>
            {
                ["pending_messages"] = metrics.PendingMessages,
                ["processed_messages"] = metrics.ProcessedMessages,
                ["failed_messages"] = metrics.FailedMessages,
                ["last_processed"] = metrics.LastProcessedTime?.ToString("O") ?? "Never",
                ["is_running"] = _processor.IsRunning
            };

            if (!_processor.IsRunning)
            {
                return HealthCheckResult.Unhealthy($"{_name}: Processor is not running", data: data);
            }

            if (metrics.PendingMessages > 1000)
            {
                return HealthCheckResult.Degraded(
                    $"{_name}: High number of pending messages",
                    data: data);
            }

            if (metrics.LastProcessedTime.HasValue && 
                DateTime.UtcNow - metrics.LastProcessedTime.Value > TimeSpan.FromMinutes(5))
            {
                return HealthCheckResult.Degraded(
                    $"{_name}: No messages processed recently",
                    data: data);
            }

            return HealthCheckResult.Healthy($"{_name}: Processor is operational", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                $"{_name}: Health check failed",
                ex,
                new Dictionary<string, object>
                {
                    ["error"] = ex.Message
                });
        }
    }
}

public class InboxProcessorHealthCheck : IHealthCheck
{
    private readonly IInboxProcessor _processor;
    private readonly string _name;

    public InboxProcessorHealthCheck(IInboxProcessor processor, string name = "inbox_processor")
    {
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _name = name;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var metrics = _processor.GetMetrics();
            
            var data = new Dictionary<string, object>
            {
                ["processed_messages"] = metrics.ProcessedMessages,
                ["duplicate_messages"] = metrics.DuplicateMessages,
                ["failed_messages"] = metrics.FailedMessages,
                ["deduplication_rate"] = metrics.DeduplicationRate,
                ["is_running"] = _processor.IsRunning
            };

            if (!_processor.IsRunning)
            {
                return HealthCheckResult.Unhealthy($"{_name}: Processor is not running", data: data);
            }

            if (metrics.DeduplicationRate > 0.5)
            {
                return HealthCheckResult.Degraded(
                    $"{_name}: High duplicate message rate",
                    data: data);
            }

            return HealthCheckResult.Healthy($"{_name}: Processor is operational", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                $"{_name}: Health check failed",
                ex,
                new Dictionary<string, object>
                {
                    ["error"] = ex.Message
                });
        }
    }
}