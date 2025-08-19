namespace HeroMessaging.Abstractions.Messages;

public interface IMessage
{
    Guid MessageId { get; }
    DateTime Timestamp { get; }
    Dictionary<string, object>? Metadata { get; }
}

public interface IMessage<TResponse> : IMessage
{
}