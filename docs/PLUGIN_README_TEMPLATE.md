# Plugin README Template

This template should be used for all HeroMessaging plugin packages.

---

# HeroMessaging.[PluginCategory].[PluginName]

**[One-line description of what this plugin provides]**

## Overview

[2-3 paragraph overview explaining:
- What this plugin does
- Why you would use it
- Key features/capabilities]

## Installation

```bash
dotnet add package HeroMessaging.[PluginCategory].[PluginName]
```

### Framework Support

- .NET 6.0
- .NET 7.0
- .NET 8.0
- .NET 9.0
- [Include .NET Standard 2.0 if supported]

## Prerequisites

[List any external dependencies or requirements:
- Database versions
- External services
- Configuration requirements
- Minimum .NET version
- etc.]

## Quick Start

### Basic Configuration

```csharp
using HeroMessaging;
using HeroMessaging.[PluginCategory].[PluginName];

services.AddHeroMessaging(builder =>
{
    builder.[ConfigurationMethod](options =>
    {
        // Minimal configuration example
        options.[RequiredSetting] = "value";
    });
});
```

### Complete Example

```csharp
// Full working example showing:
// - Setup
// - Configuration
// - Usage
// - Cleanup (if needed)
```

## Configuration

### Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `[OptionName]` | `[Type]` | `[Default]` | [Description] |
| ... | ... | ... | ... |

### Advanced Configuration

```csharp
services.AddHeroMessaging(builder =>
{
    builder.[ConfigurationMethod](options =>
    {
        // All available options with explanations
        options.[Option1] = value1;
        options.[Option2] = value2;
        // etc.
    });
});
```

## Usage Scenarios

### Scenario 1: [Common Use Case]

[Description of the scenario]

```csharp
// Code example
```

### Scenario 2: [Another Common Use Case]

[Description of the scenario]

```csharp
// Code example
```

## Performance

[If applicable, include:
- Performance characteristics
- Benchmarks
- Best practices for performance
- Resource usage]

## Troubleshooting

### Common Issues

#### Issue: [Common Problem]

**Symptoms:**
- [What the user sees]

**Solution:**
```csharp
// Fix or configuration
```

#### Issue: [Another Common Problem]

**Symptoms:**
- [What the user sees]

**Solution:**
```csharp
// Fix or configuration
```

### Logging

Enable diagnostic logging:

```csharp
services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Debug);
});
```

## Testing

### Unit Testing

```csharp
// Example of how to test with this plugin
```

### Integration Testing

```csharp
// Example of integration tests
// May include TestContainers or similar
```

## Migration

### From Version X to Y

[If applicable, include migration guides]

## See Also

- [Main Documentation](../../README.md)
- [Related Plugin 1](../[RelatedPlugin]/README.md)
- [Pattern Guide](../docs/[relevant-pattern].md)

## License

This package is part of HeroMessaging and is licensed under the MIT License.
