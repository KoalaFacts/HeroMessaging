# HeroMessaging.Serialization.Json

**High-performance JSON serialization for HeroMessaging using System.Text.Json.**

## Overview

The JSON serialization provider uses System.Text.Json for fast, modern JSON serialization with optional compression support. Ideal for most scenarios with excellent performance and .NET ecosystem integration.

## Installation

```bash
dotnet add package HeroMessaging.Serialization.Json
```

### Framework Support

- .NET Standard 2.0
- .NET 6.0
- .NET 7.0
- .NET 8.0
- .NET 9.0

## Quick Start

```csharp
using HeroMessaging;
using HeroMessaging.Serialization.Json;

services.AddHeroMessaging(builder =>
{
    builder.UseJsonSerialization();
});
```

### With Custom Options

```csharp
services.AddHeroMessaging(builder =>
{
    builder.UseJsonSerialization(options =>
    {
        options.WriteIndented = false; // Compact JSON
        options.PropertyNameCaseInsensitive = true;
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });
});
```

## Configuration

### JsonSerializerOptions

All standard `System.Text.Json` options are supported:

```csharp
builder.UseJsonSerialization(options =>
{
    options.WriteIndented = false; // Default: false for performance
    options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.Converters.Add(new JsonStringEnumConverter());
});
```

## Features

- **High Performance**: System.Text.Json optimized for speed
- **Small Payload**: Compact JSON representation
- **Type Safety**: Strong typing with .NET types
- **Extensible**: Custom converters supported
- **Cross-Platform**: Works everywhere .NET runs

## Performance

- **Serialization**: ~2-5 microseconds for typical messages
- **Deserialization**: ~3-7 microseconds for typical messages
- **Memory**: Minimal allocations with ArrayPool usage
- **Throughput**: >100K messages/second

## See Also

- [Main Documentation](../../README.md)
- [MessagePack Serialization](../HeroMessaging.Serialization.MessagePack/README.md)
- [Protobuf Serialization](../HeroMessaging.Serialization.Protobuf/README.md)

## License

This package is part of HeroMessaging and is licensed under the MIT License.
