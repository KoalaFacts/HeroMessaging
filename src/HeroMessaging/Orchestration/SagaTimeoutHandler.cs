using HeroMessaging.Abstractions.Sagas;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Orchestration;

/// <summary>
/// Background service that monitors sagas for timeouts
/// Periodically checks for stale sagas and marks them as timed out
/// </summary>
/// <typeparam name="TSaga">Type of saga being monitored</typeparam>
public class SagaTimeoutHandler<TSaga> : BackgroundService
    where TSaga : class, ISaga
{
    private readonly IServiceProvider _serviceProvider;
    private readonly SagaTimeoutOptions _options;
    private readonly ILogger<SagaTimeoutHandler<TSaga>> _logger;

    public SagaTimeoutHandler(
        IServiceProvider serviceProvider,
        SagaTimeoutOptions options,
        ILogger<SagaTimeoutHandler<TSaga>> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Saga timeout handler started for {SagaType}. Check interval: {Interval}, Default timeout: {Timeout}",
            typeof(TSaga).Name,
            _options.CheckInterval,
            _options.DefaultTimeout);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.CheckInterval, stoppingToken);
                await CheckForTimedOutSagas(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Expected during shutdown
                _logger.LogInformation("Saga timeout handler stopping for {SagaType}", typeof(TSaga).Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking for timed-out sagas of type {SagaType}", typeof(TSaga).Name);
                // Continue running despite errors
            }
        }
    }

    private async Task CheckForTimedOutSagas(CancellationToken cancellationToken)
    {
        // Create a scope to get scoped services
        using var scope = _serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISagaRepository<TSaga>>();

        _logger.LogDebug("Checking for timed-out sagas of type {SagaType}", typeof(TSaga).Name);

        // Find sagas that haven't been updated within the timeout period
        var staleSagas = await repository.FindStaleAsync(_options.DefaultTimeout, cancellationToken);
        var staleSagaList = staleSagas.ToList();

        if (staleSagaList.Count == 0)
        {
            _logger.LogDebug("No timed-out sagas found for {SagaType}", typeof(TSaga).Name);
            return;
        }

        _logger.LogWarning(
            "Found {Count} timed-out saga(s) of type {SagaType}",
            staleSagaList.Count,
            typeof(TSaga).Name);

        foreach (var saga in staleSagaList)
        {
            await HandleTimedOutSaga(saga, repository, cancellationToken);
        }
    }

    private async Task HandleTimedOutSaga(
        TSaga saga,
        ISagaRepository<TSaga> repository,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogWarning(
                "Saga {CorrelationId} timed out in state {State}. Last updated: {UpdatedAt}",
                saga.CorrelationId,
                saga.CurrentState,
                saga.UpdatedAt);

            // Mark saga as timed out by transitioning to a special "TimedOut" state
            // This allows the saga to be identified as timed out without deleting it
            var oldState = saga.CurrentState;
            saga.CurrentState = "TimedOut";
            saga.IsCompleted = true; // Mark as completed to prevent further processing

            await repository.UpdateAsync(saga, cancellationToken);

            _logger.LogInformation(
                "Marked saga {CorrelationId} as timed out. Previous state: {OldState}",
                saga.CorrelationId,
                oldState);

            // Optionally: Publish a timeout event that the saga state machine can handle
            // This would require injecting IHeroMessaging or IEventBus
            // For now, we just mark the saga as timed out
        }
        catch (SagaConcurrencyException ex)
        {
            // Saga was updated by another process - that's fine, it's no longer stale
            _logger.LogDebug(
                "Saga {CorrelationId} was updated concurrently, ignoring timeout. {Message}",
                saga.CorrelationId,
                ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to handle timeout for saga {CorrelationId}",
                saga.CorrelationId);
        }
    }
}

/// <summary>
/// Options for configuring saga timeout behavior
/// </summary>
public class SagaTimeoutOptions
{
    /// <summary>
    /// Interval at which to check for stale sagas
    /// Default: 1 minute
    /// </summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Default timeout for sagas that don't specify their own
    /// Sagas inactive longer than this will be marked as timed out
    /// Default: 24 hours
    /// </summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Whether to enable timeout handling
    /// Default: true
    /// </summary>
    public bool Enabled { get; set; } = true;
}
