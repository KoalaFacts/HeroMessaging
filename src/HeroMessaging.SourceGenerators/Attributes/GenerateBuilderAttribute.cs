namespace HeroMessaging.SourceGeneration;

/// <summary>
/// Generates a fluent builder class for the decorated message type.
/// </summary>
/// <example>
/// <code>
/// [GenerateBuilder]
/// public record OrderCreatedEvent : IEvent
/// {
///     public string OrderId { get; init; } = string.Empty;
///     public decimal Amount { get; init; }
///     public DateTime CreatedAt { get; init; }
/// }
///
/// // Generated: OrderCreatedEventBuilder
/// // Usage: var evt = OrderCreatedEventBuilder.New()
/// //                      .WithOrderId("123")
/// //                      .WithAmount(99.99m)
/// //                      .WithCreatedAt(DateTime.UtcNow)
/// //                      .Build();
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class GenerateBuilderAttribute : Attribute
{
}
