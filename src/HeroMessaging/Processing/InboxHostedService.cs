using HeroMessaging.Abstractions.Processing;
using Microsoft.Extensions.Hosting;

namespace HeroMessaging.Processing;

internal sealed class InboxHostedService(IInboxProcessor processor) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return processor.StartAsync();
    }

    public Task StopAsync(CancellationToken cancellationToken) => processor.StopAsync(cancellationToken);
}
