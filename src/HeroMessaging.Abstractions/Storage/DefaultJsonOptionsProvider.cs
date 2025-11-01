using System.Text.Json;

namespace HeroMessaging.Abstractions.Storage;

/// <summary>
/// Default JSON serializer options provider for storage implementations
/// </summary>
public class DefaultJsonOptionsProvider : IJsonOptionsProvider
{
    private static readonly JsonSerializerOptions DefaultOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    /// <inheritdoc />
    public JsonSerializerOptions GetOptions() => DefaultOptions;
}
