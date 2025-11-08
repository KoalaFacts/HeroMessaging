# HeroMessaging Architecture Tests

Automated tests that enforce architectural rules and design principles across the HeroMessaging codebase.

## Overview

These tests use [NetArchTest.Rules](https://github.com/BenMorris/NetArchTest) to validate:
- ✅ **Layer dependencies** - Abstractions don't reference implementations
- ✅ **Naming conventions** - Consistent naming across the codebase
- ✅ **Immutability** - Messages are immutable (thread-safe)
- ✅ **Handler design** - Handlers are stateless and use DI
- ✅ **General architecture** - Code organization and SOLID principles

## Test Categories

### 1. Layer Dependency Tests
Ensures proper layering and dependency direction:
- Abstractions never depend on implementation details
- Plugins are isolated (no cross-plugin dependencies)
- Storage plugins don't reference transport plugins
- Serialization plugins are independent

```csharp
[Fact]
public void Abstractions_ShouldNotDependOnImplementation()
{
    var result = Types.InAssembly(AbstractionsAssembly)
        .ShouldNot().HaveDependencyOn("HeroMessaging.Storage.SqlServer")
        .And().ShouldNot().HaveDependencyOn("HeroMessaging.Transport.RabbitMQ")
        .GetResult();

    Assert.True(result.IsSuccessful);
}
```

### 2. Naming Convention Tests
Enforces consistent naming standards:
- Interfaces start with `I`
- Commands end with `Command`
- Events end with `Event`
- Queries end with `Query`
- Handlers end with `Handler`, `Processor`, or `Decorator`
- Exceptions end with `Exception`

### 3. Immutability Tests
Validates that messages are immutable:
- Commands, Queries, Events have no public setters (except init-only)
- Message classes are sealed or abstract
- Value objects use records or structs

**Why immutability matters:**
- Thread-safe by default
- Predictable behavior (no hidden state changes)
- Safe to cache and replay
- Essential for idempotency

### 4. Handler Design Tests
Ensures handlers follow best practices:
- No public fields
- No mutable state (all fields readonly)
- Don't implement IDisposable (use DI instead)
- Use constructor injection, not property injection
- Decorators have "Decorator" in their name

**Why stateless handlers matter:**
- Can be registered as singleton in DI
- Thread-safe
- Easier to test
- No hidden coupling

### 5. General Architecture Tests
Validates overall code organization:
- Public types in proper namespaces
- Async methods end with `Async`
- Sealed classes don't have protected members
- Public classes aren't nested (discoverability)
- Each plugin has ServiceCollection extensions

## Running Tests

### Command Line
```bash
# Run all architecture tests
dotnet test --filter Category=Architecture

# Run specific test class
dotnet test --filter FullyQualifiedName~LayerDependencyTests

# Run in CI/CD
dotnet test tests/HeroMessaging.ArchitectureTests --logger "trx;LogFileName=architecture-tests.xml"
```

### Visual Studio
1. Open Test Explorer (Test → Test Explorer)
2. Filter by trait: `Category=Architecture`
3. Run All

### VS Code
1. Install C# Dev Kit extension
2. Use Testing sidebar
3. Filter by `@trait:Architecture`

## CI/CD Integration

Add to `.github/workflows/ci.yml`:

```yaml
- name: Run Architecture Tests
  run: dotnet test tests/HeroMessaging.ArchitectureTests --no-build --logger "trx"

- name: Fail on Architecture Violations
  if: failure()
  run: echo "❌ Architecture rules violated. See test results for details."
```

## What Happens When Tests Fail?

### Example Failure

```
Architecture violation detected:
HeroMessaging.Storage.SqlServer.SqlServerOutboxStorage
HeroMessaging.Storage.PostgreSql.PostgreSqlOutboxStorage

Expected: Types should not depend on HeroMessaging.Transport.RabbitMQ
Actual: 2 types have forbidden dependencies
```

### How to Fix

1. **Read the failure message** - It tells you which types violated which rule
2. **Understand the rule** - Check the test name and comments
3. **Refactor the code** - Move the offending code to the correct layer
4. **Re-run tests** - Verify the fix

### Common Violations and Fixes

**❌ Handler has mutable field:**
```csharp
public class OrderHandler : ICommandHandler<CreateOrder>
{
    private int _counter; // ❌ Mutable state
}
```

**✅ Fixed:**
```csharp
public class OrderHandler : ICommandHandler<CreateOrder>
{
    private readonly ILogger _logger; // ✅ Readonly dependency
}
```

**❌ Command with public setter:**
```csharp
public class CreateOrder : ICommand
{
    public string OrderId { get; set; } // ❌ Public setter
}
```

**✅ Fixed:**
```csharp
public class CreateOrder : ICommand
{
    public string OrderId { get; init; } // ✅ Init-only setter
}
```

**❌ Abstractions depending on implementation:**
```csharp
// In HeroMessaging.Abstractions project
using HeroMessaging.Storage.SqlServer; // ❌ Wrong direction

public interface IOutboxStorage
{
    SqlConnection GetConnection(); // ❌ Leaking implementation detail
}
```

**✅ Fixed:**
```csharp
// In HeroMessaging.Abstractions project
using System.Data; // ✅ Abstract interface

public interface IOutboxStorage
{
    IDbConnection GetConnection(); // ✅ Abstract type
}
```

## Benefits

### 1. Prevent Regression
Architecture tests catch violations automatically in CI/CD, preventing:
- Breaking abstractions
- Creating circular dependencies
- Violating naming conventions
- Adding mutable state to handlers

### 2. Onboarding
New developers learn the architecture by reading the tests:
```csharp
// Self-documenting test
[Fact]
public void Commands_ShouldNotHavePublicSetters()
{
    // This test teaches: Commands must be immutable
}
```

### 3. Living Documentation
Tests serve as executable documentation that never goes stale.

### 4. Refactoring Safety
When refactoring, architecture tests ensure you don't accidentally:
- Move code to the wrong layer
- Break dependency rules
- Violate design patterns

## Extending

### Adding New Rules

```csharp
[Fact]
[Trait("Category", "Architecture")]
public void YourNewRule()
{
    // Arrange & Act
    var result = Types.InAssembly(yourAssembly)
        .That().MeetCustomPredicate(x => /* your condition */)
        .Should().MeetCustomRule(x => /* your rule */)
        .GetResult();

    // Assert
    Assert.True(result.IsSuccessful, FormatFailureMessage(result));
}
```

### Custom Predicates

```csharp
// Find types with specific attributes
.That().HaveCustomAttribute(typeof(YourAttribute))

// Find types matching pattern
.That().HaveName(name => name.Contains("Pattern"), "contains Pattern")

// Combine conditions
.That().ArePublic().And().AreSealed()
```

### Custom Rules

```csharp
// Should conditions
.Should().BeSealed()
.Should().BeAbstract()
.Should().HaveNameEndingWith("Suffix")
.Should().ImplementInterface(typeof(IInterface))
.Should().NotHaveDependencyOn("Namespace.To.Avoid")

// Or conditions
.Should().BePublic().Or().BeInternal()
```

## Performance

Architecture tests are fast:
- ✅ No I/O operations
- ✅ No database connections
- ✅ Pure reflection-based analysis
- ✅ Typical run time: <5 seconds

## References

- [NetArchTest.Rules Documentation](https://github.com/BenMorris/NetArchTest)
- [Clean Architecture Principles](https://blog.cleancoder.com/uncle-bob/2012/08/13/the-clean-architecture.html)
- [Dependency Rule](https://martinfowler.com/articles/injection.html)
- [HeroMessaging Architecture Guide](../../docs/architecture.md)

## Troubleshooting

### Tests fail locally but pass in CI
- Check .NET SDK version (`dotnet --version`)
- Ensure all projects are built (`dotnet build`)
- Clear bin/obj folders and rebuild

### False positives
- Review the test - it may be too strict
- Consider suppression for legitimate exceptions
- Update the test to be more specific

### How to temporarily skip a test
```csharp
[Fact(Skip = "Temporarily disabled - refactoring in progress")]
public void YourTest() { }
```

## Contributing

When adding new features to HeroMessaging:

1. ✅ Run architecture tests before committing
2. ✅ Add new rules for new patterns
3. ✅ Update this README if adding test categories
4. ✅ Ensure all tests pass in CI/CD

## License

Same as HeroMessaging project (MIT).
