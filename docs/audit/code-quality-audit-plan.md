# HeroMessaging Code Quality & Architecture Audit Plan

## Overview

Comprehensive code quality and architecture audit for all 13 HeroMessaging source projects. This plan is saved to the main branch for persistence.

## Source Projects Inventory (13 Total)

| # | Project | Category | Target Frameworks | Priority |
|---|---------|----------|-------------------|----------|
| 1 | HeroMessaging.Abstractions | Core | ns2.0, net8-10 | P0 |
| 2 | HeroMessaging | Core | ns2.0, net8-10 | P0 |
| 3 | HeroMessaging.RingBuffer | Core | ns2.0, net8-10 | P1 |
| 4 | HeroMessaging.SourceGenerators | Generators | ns2.0 only | P1 |
| 5 | HeroMessaging.Security | Security | ns2.0, net8-10 | P1 |
| 6 | HeroMessaging.Storage.SqlServer | Storage | net8-10 | P1 |
| 7 | HeroMessaging.Storage.PostgreSql | Storage | net8-10 | P1 |
| 8 | HeroMessaging.Transport.RabbitMQ | Transport | ns2.0, net6-10 | P1 |
| 9 | HeroMessaging.Serialization.Json | Serialization | ns2.0, net8-10 | P2 |
| 10 | HeroMessaging.Serialization.MessagePack | Serialization | net8-10 | P2 |
| 11 | HeroMessaging.Serialization.Protobuf | Serialization | net8-10 | P2 |
| 12 | HeroMessaging.Observability.HealthChecks | Observability | ns2.0, net8-10 | P2 |
| 13 | HeroMessaging.Observability.OpenTelemetry | Observability | net8-10 | P2 |

---

## Audit Checklist Per Project

### A. Code Quality Checks

1. **TimeProvider Usage** (Critical)
   - [ ] No `DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.Now`, `DateTimeOffset.UtcNow` in production code
   - [ ] TimeProvider injected via constructor for time-sensitive operations
   - [ ] Exception: DTOs with default property values are acceptable

2. **Async/Await Patterns** (Critical)
   - [ ] No blocking calls: `.Result`, `.Wait()`, `.GetAwaiter().GetResult()` in async context
   - [ ] `ConfigureAwait(false)` on all library async calls
   - [ ] Exception: `Dispose()` calling `DisposeAsync()` is acceptable pattern

3. **Framework Compatibility** (Critical for ns2.0)
   - [ ] No C# 8+ Index/Range syntax (`[^1]`, `[..]`) in netstandard2.0 projects
   - [ ] PolySharp polyfills properly configured
   - [ ] Conditional compilation for framework-specific features

4. **Lazy Initialization** (Storage projects)
   - [ ] `EnsureInitializedAsync()` pattern with `SemaphoreSlim` double-check
   - [ ] No blocking database calls in constructors
   - [ ] Respects `AutoCreateTables` option

5. **SQL Injection Prevention** (Storage projects)
   - [ ] `ValidateSqlIdentifier()` for schema/table names
   - [ ] Parameterized queries for all user input
   - [ ] No string concatenation with user data

6. **Resource Management**
   - [ ] `IDisposable`/`IAsyncDisposable` properly implemented
   - [ ] Connection pooling where applicable
   - [ ] No resource leaks in exception paths

7. **Error Handling**
   - [ ] Actionable error messages with error codes
   - [ ] Proper exception hierarchy
   - [ ] CancellationToken propagation

### B. Architecture Checks

1. **Dependency Direction**
   - [ ] No circular dependencies
   - [ ] Plugins depend only on Abstractions (not Core)
   - [ ] Core depends on Abstractions + RingBuffer only

2. **Interface Segregation**
   - [ ] Small, focused interfaces
   - [ ] No "god" interfaces
   - [ ] Proper abstraction levels

3. **Extension Points**
   - [ ] Builder pattern for configuration
   - [ ] Decorator support in pipeline
   - [ ] Plugin registration via DI

4. **Naming Conventions**
   - [ ] Extension classes: `ExtensionsTo{TargetType}`
   - [ ] Test classes: `{ClassUnderTest}Tests`
   - [ ] Follows .editorconfig rules

---

## Per-Project Audit Details

### 1. HeroMessaging.Abstractions
**Path**: `src/HeroMessaging.Abstractions`
**Role**: Core interfaces and contracts for entire system

**Audit Focus**:
- Interface design quality
- DTO immutability patterns
- Generic constraint appropriateness
- Attribute definitions for source generators

**Known Issues to Check**:
- [ ] `DateTimeOffset.UtcNow` in DTO defaults (acceptable)
- [ ] Proper nullable annotations

**Key Files**:
- `IHeroMessaging.cs` - Main facade interface
- `IMessageProcessor.cs` - Processing abstraction
- `Storage/*.cs` - Storage abstractions
- `Transport/*.cs` - Transport abstractions

---

### 2. HeroMessaging (Core)
**Path**: `src/HeroMessaging`
**Role**: Main implementation of messaging patterns

**Audit Focus**:
- Decorator pipeline implementation
- Processing constants consolidation
- Error handling and retry logic
- TimeProvider usage throughout

**Known Issues to Check**:
- [ ] Ring buffer event handler sync constraints (acceptable)
- [ ] `Dispose()` calling `DisposeAsync()` (acceptable pattern)

**Key Directories**:
- `Processing/` - Pipeline and decorators
- `Idempotency/` - Response caching
- `Policies/` - Rate limiting
- `Resilience/` - Circuit breaker, retry

---

### 3. HeroMessaging.RingBuffer
**Path**: `src/HeroMessaging.RingBuffer`
**Role**: High-performance lock-free ring buffer (Disruptor pattern)

**Audit Focus**:
- Thread safety verification
- Memory ordering correctness
- Performance characteristics
- API surface minimal and clean

**Key Files**:
- `RingBuffer.cs` - Main implementation
- `ProducerType.cs` - Producer enumeration

---

### 4. HeroMessaging.SourceGenerators
**Path**: `src/HeroMessaging.SourceGenerators`
**Role**: Roslyn source generators for code reduction

**Audit Focus**:
- netstandard2.0 compatibility (CRITICAL)
- No C# 8+ syntax without polyfills
- Generator diagnostics quality
- Generated code quality

**Known Issues Fixed**:
- [x] `parts[^1]` -> `parts[parts.Length - 1]` (Index syntax)

**Key Files**:
- `Generators/SagaDslGenerator.cs`
- `Generators/ValidatorGenerator.cs`
- `Generators/HandlerRegistrationGenerator.cs`

---

### 5. HeroMessaging.Security
**Path**: `src/HeroMessaging.Security`
**Role**: Encryption, signing, auth

**Audit Focus**:
- Crypto best practices (AES-GCM, HMAC-SHA256)
- Key management patterns
- Security context propagation
- No sensitive data in logs

**Key Files**:
- `Encryption/AesGcmMessageEncryptor.cs`
- `Signing/HmacSha256MessageSigner.cs`
- `Authentication/ClaimsAuthenticationProvider.cs`

---

### 6. HeroMessaging.Storage.SqlServer
**Path**: `src/HeroMessaging.Storage.SqlServer`
**Role**: SQL Server persistence

**Audit Focus**:
- Lazy initialization pattern (CRITICAL)
- SQL injection prevention
- Connection management
- Transaction handling

**Fixed Issues**:
- [x] Blocking constructors -> Lazy init pattern

**Key Files**:
- `SqlServerOutboxStorage.cs`
- `SqlServerInboxStorage.cs`
- `SqlServerQueueStorage.cs`
- `SqlServerDeadLetterQueue.cs`
- `SqlServerSagaRepository.cs`

---

### 7. HeroMessaging.Storage.PostgreSql
**Path**: `src/HeroMessaging.Storage.PostgreSql`
**Role**: PostgreSQL persistence

**Audit Focus**:
- Same as SqlServer
- Npgsql-specific patterns
- JSONB usage for payloads

**Fixed Issues**:
- [x] Lazy initialization pattern complete

**Key Files**:
- `PostgreSqlOutboxStorage.cs`
- `PostgreSqlInboxStorage.cs`
- `PostgreSqlQueueStorage.cs`
- `PostgreSqlDeadLetterQueue.cs`

---

### 8. HeroMessaging.Transport.RabbitMQ
**Path**: `src/HeroMessaging.Transport.RabbitMQ`
**Role**: RabbitMQ message transport

**Audit Focus**:
- Connection pooling
- Channel management
- Publisher confirms
- Reconnection logic
- Consumer lifecycle

**Key Files**:
- `RabbitMqTransport.cs`
- `RabbitMqConsumer.cs`
- `Connection/RabbitMqChannelPool.cs`
- `Connection/RabbitMqConnectionPool.cs`

---

### 9-11. Serialization Projects
**Paths**:
- `src/HeroMessaging.Serialization.Json`
- `src/HeroMessaging.Serialization.MessagePack`
- `src/HeroMessaging.Serialization.Protobuf`

**Audit Focus**:
- Span-based serialization efficiency
- Compression integration
- Type handling
- Size limits enforcement

**Key Pattern**: All serializers implement `IMessageSerializer`

---

### 12-13. Observability Projects
**Paths**:
- `src/HeroMessaging.Observability.HealthChecks`
- `src/HeroMessaging.Observability.OpenTelemetry`

**Audit Focus**:
- Health check accuracy
- Metric naming conventions
- Trace context propagation
- Activity tagging

---

## Execution Strategy

### Phase 1: Setup (Save plan to repo)
1. Save this audit plan to `docs/audit/code-quality-audit-plan.md`
2. Create worktrees for each project category

### Phase 2: Core Projects Audit
- Abstractions, Core, RingBuffer
- Focus: Interface design, TimeProvider, async patterns

### Phase 3: Infrastructure Projects Audit
- SourceGenerators, Security
- Focus: ns2.0 compatibility, crypto patterns

### Phase 4: Storage Projects Audit
- SqlServer, PostgreSql
- Focus: Lazy init, SQL injection, transactions

### Phase 5: Transport Projects Audit
- RabbitMQ
- Focus: Connection management, reliability

### Phase 6: Plugin Projects Audit
- Serialization (3), Observability (2)
- Focus: Interface compliance, performance

### Phase 7: Cross-Cutting Concerns
- Dependency graph validation
- Naming convention enforcement
- Documentation completeness

---

## Worktree Strategy

Create worktrees by category to enable parallel work:
```bash
git worktree add ../HeroMessaging-audit-core audit/core
git worktree add ../HeroMessaging-audit-storage audit/storage
git worktree add ../HeroMessaging-audit-transport audit/transport
git worktree add ../HeroMessaging-audit-serialization audit/serialization
git worktree add ../HeroMessaging-audit-observability audit/observability
```

---

## Deliverables

1. **Per-Project Audit Report** (`docs/audit/{project}-audit.md`)
   - Issues found
   - Fixes applied
   - Recommendations

2. **Architecture Diagram** (`docs/architecture/dependency-graph.md`)
   - Project dependencies
   - Layer violations if any

3. **Quality Metrics Summary** (`docs/audit/quality-summary.md`)
   - Coverage by project
   - Issue counts by severity
   - Before/after comparisons
