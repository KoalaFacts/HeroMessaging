using System.Text.Json;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Utilities;
using Microsoft.Extensions.DependencyInjection;

namespace HeroMessaging.Idempotency;

/// <summary>
/// Turns a cached idempotency result back into the response type of a command or query.
/// </summary>
/// <remarks>
/// A response produced in this process is returned as is. A durable store (PostgreSQL, SQL Server) reads the response
/// back as a <see cref="JsonElement"/>; it is converted with the registered <see cref="IJsonSerializer"/>, matching the
/// stores' case-insensitive reading. Anything else raises <c>[HM-IDEMPOTENCY-RESPONSE]</c> with remediation.
/// </remarks>
internal static class CachedResponseConverter
{
    private static readonly JsonSerializerOptions CachedResponseJsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static TResponse Convert<TResponse>(object? data, IServiceProvider serviceProvider, string kind, IMessage message)
    {
        switch (data)
        {
            case TResponse response:
                return response;
            case null:
                return default!;
            case JsonElement json:
                var serializer = serviceProvider.GetService<IJsonSerializer>()
                    ?? throw new InvalidOperationException(
                        $"[HM-IDEMPOTENCY-RESPONSE] The cached response for {kind} {message.GetType().Name} " +
                        $"(message {message.MessageId}) is JSON, but no {nameof(IJsonSerializer)} is registered to convert it " +
                        $"to {typeof(TResponse).Name}. Register HeroMessaging with AddHeroMessaging(), which adds the default serializer.");
                try
                {
                    return serializer.DeserializeFromString<TResponse>(json.GetRawText(), CachedResponseJsonOptions)!;
                }
                catch (Exception ex) when (ex is JsonException or NotSupportedException)
                {
                    throw new InvalidOperationException(
                        $"[HM-IDEMPOTENCY-RESPONSE] The cached response for {kind} {message.GetType().Name} " +
                        $"(message {message.MessageId}) could not be converted from JSON to {typeof(TResponse).Name}: {ex.Message} " +
                        "Use a response type that round-trips through System.Text.Json (public constructor or settable " +
                        "properties, no interface or abstract type), or clear the idempotency record for this message.", ex);
                }
            default:
                throw new InvalidOperationException(
                    $"[HM-IDEMPOTENCY-RESPONSE] The cached response for {kind} {message.GetType().Name} " +
                    $"(message {message.MessageId}) is a {data.GetType().Name}, not a {typeof(TResponse).Name}. " +
                    "Clear the idempotency record for this message, or make the idempotency store return the stored " +
                    "response type or its JSON.");
        }
    }
}
