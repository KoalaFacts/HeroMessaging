using HeroMessaging.Abstractions.Idempotency;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;

namespace HeroMessaging.Idempotency;

/// <summary>
/// Wraps an <see cref="IIdempotencyPolicy"/> so each handler of a message gets its own idempotency key.
/// </summary>
/// <remarks>
/// An event fans out to several handlers that share one <see cref="IMessage.MessageId"/>. Keying only by
/// message would let the first handler's cached response suppress every other handler, so the configured
/// key is suffixed with the handler's runtime type. Contexts without a handler keep the configured key.
/// </remarks>
internal sealed class HandlerScopedIdempotencyPolicy(IIdempotencyPolicy inner) : IIdempotencyPolicy, IIdempotencyKeyGenerator
{
    public IIdempotencyPolicy Inner { get; } = inner ?? throw new ArgumentNullException(nameof(inner));

    public TimeSpan SuccessTtl => Inner.SuccessTtl;

    public TimeSpan FailureTtl => Inner.FailureTtl;

    public bool CacheFailures => Inner.CacheFailures;

    public IIdempotencyKeyGenerator KeyGenerator => this;

    public bool IsIdempotentFailure(Exception exception) => Inner.IsIdempotentFailure(exception);

    public string GenerateKey(IMessage message, ProcessingContext context)
    {
        var key = Inner.KeyGenerator.GenerateKey(message, context);
        var handlerType = context.Handler?.GetType();
        return handlerType is null ? key : $"{key}:handler:{handlerType.FullName}";
    }
}
