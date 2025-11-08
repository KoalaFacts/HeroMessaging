# Code Quality Improvement Plan

## Overview

The `.editorconfig` now contains 400+ code analysis rules, but uses **gradual enforcement** to avoid overwhelming developers with warnings. This document outlines a systematic approach to improve code quality over time.

## Current Severity Levels

### 🔴 Error (Must Fix Immediately)
- **Security vulnerabilities** (CA2300-CA5403): Insecure deserialization, weak crypto, SQL injection
- **Critical bugs** (CA2011, CA2013, CA2014, CA2017, CA2018, CA2245, CA2246)
- **Disposal violations** (CA1001, CA1063, CA2215)
- **Exception handling** (CA2200, CA2208, CA2219)

**Action:** Fix these before any production deployment.

### ⚠️ Warning (Fix When Touching Code)
- **Interface naming** (IDE1006): Interfaces must start with 'I'
- **Invalid format strings** (IDE0043)
- **Unreachable code** (IDE0035)
- **Key CA rules** (CA1052, CA1065, etc.)

**Action:** Fix when modifying nearby code.

### 💡 Suggestion (IDE Hints Only)
- **Naming conventions**: PascalCase, camelCase, _camelCase for privates
- **Modern C# features**: Primary constructors, file-scoped namespaces, collection expressions
- **Code simplification**: Use pattern matching, null-coalescing, range operators
- **Performance tips**: Use Span\<T\>, avoid LINQ allocations

**Action:** Apply opportunistically. IDE will show lightbulb hints.

### ⚪ Silent (Not Enforced Yet)
- **Formatting rules** (IDE2000-IDE2006): Too opinionated for now
- **Some style rules**: Will enable after team agrees on conventions

**Action:** Can be promoted to 'suggestion' in future.

## Systematic Fix Strategy

### Phase 1: Security & Correctness (Week 1)
**Goal:** Zero errors in build output

```bash
# 1. Build and capture all errors
dotnet build 2>&1 | grep "error CA\|error CS" > errors.txt

# 2. Fix by category:
# - CA2300-CA5403: Security (deserialization, crypto)
# - CA2200-CA2259: Exception handling
# - CA1001, CA1063, CA2215: Disposal
# - CA2011-CA2018: Logic bugs
```

**Common fixes:**
```csharp
// CA2200: Rethrow preserving stack
catch (Exception)
{
    throw; // ✅ Correct
    // throw ex; ❌ Loses stack trace
}

// CA2208: Argument exceptions
throw new ArgumentNullException(nameof(parameter)); // ✅
// throw new ArgumentNullException("parameter"); ❌ Magic string

// CA2219: Don't throw in finally
finally
{
    // ❌ Never throw here - masks original exception
}

// CA5350: Use strong crypto
using var sha256 = SHA256.Create(); // ✅
// using var md5 = MD5.Create(); ❌ Broken algorithm
```

### Phase 2: Performance (Week 2)
**Goal:** Apply zero-allocation and Span\<T\> optimizations

```bash
# Find performance issues
dotnet build 2>&1 | grep "warning CA18[0-9][0-9]"
```

**Common fixes:**
```csharp
// CA1827: Use Any() instead of Count()
if (list.Any()) // ✅
// if (list.Count() > 0) ❌ Enumerates entire collection

// CA1829: Use Length/Count property
var count = array.Length; // ✅
// var count = array.Count(); ❌ Unnecessary LINQ call

// CA1846: Prefer AsSpan over Substring
ReadOnlySpan<char> span = text.AsSpan(0, 10); // ✅ Zero allocation
// string sub = text.Substring(0, 10); ❌ Allocates new string

// CA1851: Avoid multiple enumerations
var items = source.ToList(); // ✅ Enumerate once
var first = items.First();
var count = items.Count;
// var first = source.First(); ❌ Enumerates
// var count = source.Count(); ❌ Enumerates again

// CA1861: Avoid constant arrays as arguments
private static readonly int[] Values = { 1, 2, 3 }; // ✅
void Method() => Process(Values);
// void Method() => Process(new[] { 1, 2, 3 }); ❌ Allocates every call
```

### Phase 3: Naming Conventions (Week 3)
**Goal:** Consistent naming across codebase

```bash
# Find naming violations
dotnet build 2>&1 | grep "suggestion.*naming\|IDE1006"
```

**Naming standards:**
```csharp
// Interfaces
public interface IMessageProcessor { } // ✅ I prefix

// Type parameters
public class Repository<TEntity> { } // ✅ T prefix

// Private fields
private readonly ILogger _logger; // ✅ _camelCase

// Public fields (avoid, use properties instead)
public const int MaxRetries = 3; // ✅ PascalCase

// Methods, properties
public void ProcessMessage() { } // ✅ PascalCase
public int RetryCount { get; set; } // ✅ PascalCase

// Parameters, locals
void Method(string messageId) // ✅ camelCase
{
    var processor = CreateProcessor(); // ✅ camelCase
}
```

### Phase 4: Modern C# Patterns (Week 4)
**Goal:** Adopt C# 10-12 features

```bash
# Find modernization opportunities
dotnet build 2>&1 | grep "IDE0160\|IDE0161\|IDE0290"
```

**Common upgrades:**
```csharp
// IDE0160: File-scoped namespaces (C# 10)
namespace HeroMessaging.Processing; // ✅

public class Processor { }

// vs traditional
// namespace HeroMessaging.Processing ❌
// {
//     public class Processor { }
// }

// IDE0290: Primary constructors (C# 12)
public class MessageProcessor(
    ILogger<MessageProcessor> logger,
    IMessageStore store) : IMessageProcessor // ✅
{
    public void Process()
    {
        logger.LogInformation("Processing...");
        store.Save();
    }
}

// vs traditional
// public class MessageProcessor : IMessageProcessor ❌
// {
//     private readonly ILogger<MessageProcessor> _logger;
//     private readonly IMessageStore _store;
//
//     public MessageProcessor(ILogger<MessageProcessor> logger, IMessageStore store)
//     {
//         _logger = logger;
//         _store = store;
//     }
// }

// Collection expressions (C# 12)
int[] numbers = [1, 2, 3]; // ✅
// int[] numbers = new[] { 1, 2, 3 }; ❌

// Pattern matching
if (obj is string { Length: > 0 } str) // ✅
{
    Process(str);
}
```

## Automated Fixes

Many rules have automatic fixes in the IDE:

### Visual Studio
1. **Ctrl+.** on any squiggly → "Fix all in document/project/solution"
2. **Analyze** → **Code Cleanup** → **Run Code Cleanup (Profile 1)**

### VS Code with C# extension
1. **Ctrl+.** on diagnostic → "Fix all in document"
2. **Command Palette** → "Format Document"

### Rider
1. **Alt+Enter** → "Fix all in file"
2. **Code** → **Cleanup Code** → **Full Cleanup**

### CLI (dotnet format)
```bash
# Format entire solution
dotnet format

# Apply analyzers (requires .NET 6+)
dotnet format analyzers

# Preview changes without applying
dotnet format --verify-no-changes
```

## Tracking Progress

### Run analysis reports
```bash
# Generate HTML report
dotnet build /p:RunCodeAnalysis=true
# Creates bin/CodeAnalysis/CodeAnalysisReport.html

# Or use Roslyn analyzers
dotnet build /p:EnforceCodeStyleInBuild=true
```

### CI/CD Integration

Add to `.github/workflows/ci.yml`:
```yaml
- name: Check code quality
  run: |
    dotnet format --verify-no-changes
    dotnet build --no-incremental /warnaserror
```

## Promotion Schedule

As violations decrease, increase enforcement:

| Rule Category | Current | Target (Q1) | Target (Q2) |
|---------------|---------|-------------|-------------|
| Security (CA2300-CA5403) | error | error | error |
| Disposal (CA1001, CA1063) | error | error | error |
| Performance (CA18xx) | warning | warning | error |
| Naming (IDE1006) | suggestion | warning | warning |
| Modern C# (IDE0160, IDE0290) | suggestion | suggestion | warning |
| Formatting (IDE2xxx) | silent | suggestion | warning |

## Quick Wins

Fix these for immediate improvement with minimal effort:

1. **Unused using directives** (IDE0005) - One click in IDE
2. **var when type is obvious** (IDE0008) - Automatic fix
3. **File-scoped namespaces** (IDE0161) - Automatic in .NET 6+
4. **Primary constructors** (IDE0290) - Refactor tool available
5. **Collection expressions** (IDE0300-IDE0305) - C# 12 feature

## Team Agreement

Before promoting severities, ensure team agrees on:

1. **Naming conventions** - Especially for test projects
2. **Modern C# adoption** - Are we using C# 12 features?
3. **Formatting preferences** - Allman braces vs K&R, etc.
4. **Suppression policy** - When is `#pragma warning disable` acceptable?

Document decisions in `CLAUDE.md` or separate `CODE_STYLE.md`.

## Resources

- [.NET Code Quality Rules](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/)
- [.NET Code Style Rules](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/style-rules/)
- [EditorConfig Documentation](https://editorconfig.org/)
- [Roslyn Analyzers](https://github.com/dotnet/roslyn-analyzers)

## Next Steps

1. ✅ .editorconfig created with gradual enforcement
2. ⏳ Run `dotnet build` and fix all errors (Phase 1)
3. ⏳ Apply quick wins (unused usings, var, etc.)
4. ⏳ Create baseline suppressions for legacy code if needed
5. ⏳ Set up CI/CD quality gates
6. ⏳ Schedule quarterly reviews to promote severity levels
