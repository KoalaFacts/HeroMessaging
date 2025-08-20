using System.Data;
using HeroMessaging.Abstractions.Storage;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Core.Resilience;

/// <summary>
/// Factory decorator that creates resilient UnitOfWork instances
/// </summary>
public class ResilientUnitOfWorkFactory : IUnitOfWorkFactory
{
    private readonly IUnitOfWorkFactory _inner;
    private readonly IConnectionResiliencePolicy _resiliencePolicy;
    private readonly ILogger<ResilientUnitOfWorkFactory> _logger;

    public ResilientUnitOfWorkFactory(
        IUnitOfWorkFactory inner,
        IConnectionResiliencePolicy resiliencePolicy,
        ILogger<ResilientUnitOfWorkFactory> logger)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _resiliencePolicy = resiliencePolicy ?? throw new ArgumentNullException(nameof(resiliencePolicy));
        _logger = logger;
    }

    public async Task<IUnitOfWork> CreateAsync(CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
        {
            var unitOfWork = await _inner.CreateAsync(cancellationToken);
            return new ConnectionResilienceDecorator(unitOfWork, _resiliencePolicy, 
                _logger.CreateLogger<ConnectionResilienceDecorator>());
        }, "CreateUnitOfWork", cancellationToken);
    }

    public async Task<IUnitOfWork> CreateAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
        {
            var unitOfWork = await _inner.CreateAsync(isolationLevel, cancellationToken);
            return new ConnectionResilienceDecorator(unitOfWork, _resiliencePolicy,
                _logger.CreateLogger<ConnectionResilienceDecorator>());
        }, "CreateUnitOfWork", cancellationToken);
    }
}