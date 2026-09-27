using HeroMessaging.Abstractions.Processing;
using Microsoft.Extensions.Hosting;

namespace HeroMessaging.Processing;

internal sealed class OutboxHostedService(IOutboxProcessor processor) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // The startup token is not the application lifetime token.
        return processor.StartAsync();
    }

    public Task StopAsync(CancellationToken cancellationToken) => processor.StopAsync(cancellationToken);
}
