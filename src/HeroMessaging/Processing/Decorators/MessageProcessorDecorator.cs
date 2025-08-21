using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;

namespace HeroMessaging.Processing.Decorators;

/// <summary>
/// Base decorator for message processors
/// </summary>
public abstract class MessageProcessorDecorator(IMessageProcessor inner) : IMessageProcessor
{
    protected readonly IMessageProcessor _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public virtual async ValueTask<ProcessingResult> ProcessAsync(IMessage message, ProcessingContext context, CancellationToken cancellationToken = default)
    {
        return await _inner.ProcessAsync(message, context, cancellationToken);
    }
}