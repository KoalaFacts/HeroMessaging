namespace HeroMessaging.SourceGeneration;

/// <summary>
/// Generates an idempotency key generator based on specified properties.
/// </summary>
/// <example>
/// <code>
/// [GenerateIdempotencyKey(nameof(OrderId), nameof(CustomerId))]
/// public record CreateOrderCommand : ICommand
/// {
///     public string OrderId { get; init; } = string.Empty;
///     public string CustomerId { get; init; } = string.Empty;
///     public decimal Amount { get; init; }
/// }
///
/// // Generated: IIdempotencyKeyGenerator implementation
/// // Key format: "idempotency:CreateOrderCommand:{OrderId}:{CustomerId}"
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class GenerateIdempotencyKeyAttribute : Attribute
{
    /// <summary>
    /// Property names to include in the idempotency key.
    /// </summary>
    public string[] PropertyNames { get; }

    public GenerateIdempotencyKeyAttribute(params string[] propertyNames)
    {
        PropertyNames = propertyNames ?? Array.Empty<string>();
    }
}
