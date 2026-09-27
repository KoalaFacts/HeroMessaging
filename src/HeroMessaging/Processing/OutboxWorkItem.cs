using HeroMessaging.Abstractions.Storage;

namespace HeroMessaging.Processing;

/// <summary>A queued local message or a claimed external message.</summary>
public sealed record OutboxWorkItem(OutboxEntry Entry, Guid? LeaseToken = null);
