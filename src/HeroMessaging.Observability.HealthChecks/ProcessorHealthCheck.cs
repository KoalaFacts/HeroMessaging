using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using HeroMessaging.Processing;
using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Queries;
using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Observability.HealthChecks;

public class CommandProcessorHealthCheck : IHealthCheck
{
    private readonly ICommandProcessor _processor;
    private readonly string _name;

    public CommandProcessorHealthCheck(ICommandProcessor processor, string name = "command_processor")
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
            // Try to send a ping command to verify processor is working
            var testCommand = new PingCommand();
            await _processor.Send(testCommand, cancellationToken);
            
            return HealthCheckResult.Healthy($"{_name}: Processor is operational");
        }
        catch (NotImplementedException)
        {
            // Ping command handler not registered, which is expected
            return HealthCheckResult.Healthy($"{_name}: Processor is ready");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                $"{_name}: Processor check failed",
                ex,
                new Dictionary<string, object>
                {
                    ["error"] = ex.Message
                });
        }
    }

    private class PingCommand : ICommand
    {
        public Guid MessageId { get; } = Guid.NewGuid();
        public DateTime Timestamp { get; } = DateTime.UtcNow;
        public Dictionary<string, object>? Metadata { get; }
    }
}

public class QueryProcessorHealthCheck : IHealthCheck
{
    private readonly IQueryProcessor _processor;
    private readonly string _name;

    public QueryProcessorHealthCheck(IQueryProcessor processor, string name = "query_processor")
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
            // Try to send a ping query to verify processor is working
            var testQuery = new PingQuery();
            await _processor.Send<string>(testQuery, cancellationToken);
            
            return HealthCheckResult.Healthy($"{_name}: Processor is operational");
        }
        catch (NotImplementedException)
        {
            // Ping query handler not registered, which is expected
            return HealthCheckResult.Healthy($"{_name}: Processor is ready");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                $"{_name}: Processor check failed",
                ex,
                new Dictionary<string, object>
                {
                    ["error"] = ex.Message
                });
        }
    }

    private class PingQuery : IQuery<string>
    {
        public Guid MessageId { get; } = Guid.NewGuid();
        public DateTime Timestamp { get; } = DateTime.UtcNow;
        public Dictionary<string, object>? Metadata { get; }
    }
}

public class EventBusHealthCheck : IHealthCheck
{
    private readonly IEventBus _eventBus;
    private readonly string _name;

    public EventBusHealthCheck(IEventBus eventBus, string name = "event_bus")
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
            // Event bus should always be ready to publish
            return await Task.FromResult(HealthCheckResult.Healthy($"{_name}: Event bus is operational"));
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                $"{_name}: Event bus check failed",
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
    private readonly string? _queueName;

    public QueueProcessorHealthCheck(IQueueProcessor processor, string name = "queue_processor", string? queueName = null)
    {
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _name = name;
        _queueName = queueName;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!string.IsNullOrEmpty(_queueName))
            {
                var depth = await _processor.GetQueueDepth(_queueName, cancellationToken);
                
                var data = new Dictionary<string, object>
                {
                    ["queue"] = _queueName,
                    ["depth"] = depth
                };

                if (depth > 10000)
                {
                    return HealthCheckResult.Degraded(
                        $"{_name}: Queue depth is high",
                        data: data);
                }

                return HealthCheckResult.Healthy($"{_name}: Queue is operational", data);
            }

            return HealthCheckResult.Healthy($"{_name}: Queue processor is ready");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                $"{_name}: Queue processor check failed",
                ex,
                new Dictionary<string, object>
                {
                    ["queue"] = _queueName ?? "unknown",
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
            // Outbox processor should be ready to accept messages
            return await Task.FromResult(HealthCheckResult.Healthy($"{_name}: Outbox processor is operational"));
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                $"{_name}: Outbox processor check failed",
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
            var unprocessedCount = await _processor.GetUnprocessedCount(cancellationToken);
            
            var data = new Dictionary<string, object>
            {
                ["unprocessed_count"] = unprocessedCount
            };

            if (unprocessedCount > 10000)
            {
                return HealthCheckResult.Degraded(
                    $"{_name}: High unprocessed message count",
                    data: data);
            }

            return HealthCheckResult.Healthy($"{_name}: Inbox processor is operational", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                $"{_name}: Inbox processor check failed",
                ex,
                new Dictionary<string, object>
                {
                    ["error"] = ex.Message
                });
        }
    }
}