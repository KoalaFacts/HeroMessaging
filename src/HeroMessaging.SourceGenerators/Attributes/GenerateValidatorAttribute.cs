namespace HeroMessaging.SourceGeneration;

/// <summary>
/// Generates a validator class for the decorated message type based on data annotations.
/// </summary>
/// <example>
/// <code>
/// [GenerateValidator]
/// public record CreateOrderCommand : ICommand
/// {
///     [Required, MaxLength(50)]
///     public string OrderId { get; init; } = string.Empty;
///
///     [Range(0.01, 1000000)]
///     public decimal Amount { get; init; }
/// }
///
/// // Generated: CreateOrderCommandValidator
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class GenerateValidatorAttribute : Attribute
{
}
