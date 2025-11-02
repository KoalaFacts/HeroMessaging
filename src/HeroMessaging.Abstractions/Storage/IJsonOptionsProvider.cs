using System.Text.Json;

namespace HeroMessaging.Abstractions.Storage;

/// <summary>
/// Provides JsonSerializerOptions for storage serialization
/// </summary>
public interface IJsonOptionsProvider
{
    /// <summary>
    /// Gets the JsonSerializerOptions to use for serialization
    /// </summary>
    /// <returns>Configured JsonSerializerOptions</returns>
    JsonSerializerOptions GetOptions();
}
