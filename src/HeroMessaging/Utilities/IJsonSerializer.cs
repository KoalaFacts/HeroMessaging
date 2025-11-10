using System;
using System.Buffers;
using System.Text.Json;

namespace HeroMessaging.Utilities;

/// <summary>
/// Interface for efficient JSON serialization using span-based APIs.
/// Reduces allocations in hot paths like storage and validation.
/// </summary>
public interface IJsonSerializer
{
    /// <summary>
    /// Serializes an object to JSON and returns the UTF-8 string.
    /// Uses ArrayBufferWriter for zero-allocation byte generation.
    /// </summary>
    string SerializeToString(object value, JsonSerializerOptions? options = null);

    /// <summary>
    /// Serializes an object to JSON and returns the UTF-8 string.
    /// Generic version for better performance when type is known at compile time.
    /// </summary>
    string SerializeToString<T>(T value, JsonSerializerOptions? options = null);

    /// <summary>
    /// Serializes an object to JSON and returns the UTF-8 string.
    /// Overload that accepts a runtime Type for polymorphic scenarios.
    /// </summary>
    string SerializeToString(object value, Type type, JsonSerializerOptions? options = null);

    /// <summary>
    /// Serializes an object to JSON UTF-8 bytes and writes to an ArrayBufferWriter.
    /// Allows caller to reuse the buffer or convert to string as needed.
    /// NOTE: Not available in netstandard2.0 due to ArrayBufferWriter accessibility.
    /// </summary>
#if !NETSTANDARD2_0
    void SerializeToBuffer<T>(T value, ArrayBufferWriter<byte> buffer, JsonSerializerOptions? options = null);
#endif

    /// <summary>
    /// Deserializes JSON from a UTF-8 string using span-based APIs with pooled buffers.
    /// Uses stack allocation for small JSON, pooled buffers for larger JSON.
    /// Most efficient option for deserialization hot paths.
    /// </summary>
    T? DeserializeFromString<T>(string json, JsonSerializerOptions? options = null);

    /// <summary>
    /// Deserializes JSON from a UTF-8 string to a specific type using pooled buffers.
    /// Uses stack allocation for small JSON, pooled buffers for larger JSON.
    /// </summary>
    object? DeserializeFromString(string json, Type type, JsonSerializerOptions? options = null);

    /// <summary>
    /// Gets the size in bytes of the JSON representation without creating a string.
    /// Useful for validation and metrics.
    /// </summary>
    int GetJsonByteCount<T>(T value, JsonSerializerOptions? options = null);
}
