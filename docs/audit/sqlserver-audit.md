# HeroMessaging.Storage.SqlServer Code Quality Audit Report

**Audit Date**: 2025-11-28
**Overall Risk Level**: Medium

## Summary

| Metric | Value |
|--------|-------|
| Critical Issues | 0 (1 Fixed) |
| High Priority Issues | 3 |
| Medium Priority Issues | 5 |
| Low Priority Issues | 3 |

## Lazy Initialization Verification

| Class | Status |
|-------|--------|
| SqlServerOutboxStorage | PASS |
| SqlServerInboxStorage | PASS |
| SqlServerQueueStorage | PASS |
| SqlServerDeadLetterQueue | PASS |
| SqlServerSagaRepository | PASS |
| SqlServerMessageStorage | PASS |
| SqlServerIdempotencyStore | PASS (Fixed) |

## Critical Issues

### 1. SqlServerIdempotencyStore Missing Lazy Initialization - **FIXED**

**File**: `SqlServerIdempotencyStore.cs`

~~Missing `SemaphoreSlim`, `_initialized`, and `EnsureInitializedAsync()`. Requires pre-existing tables.~~

**Status**: ✅ FIXED - Added lazy initialization pattern with `SemaphoreSlim`, `_initialized`, `EnsureInitializedAsync()`, and `InitializeDatabaseAsync()`. All methods now use `_tableName` variable and call `EnsureInitializedAsync()`.

## High Priority Issues

### 1. SQL Injection in SqlServerOutboxStorage

**File**: `SqlServerOutboxStorage.cs:111-144`

Schema/table names interpolated without validation.

**Fix**: Add `ValidateSqlIdentifier()` method.

### 2. SQL Injection in SqlServerQueueStorage

**File**: `SqlServerQueueStorage.cs:104-136`

Same pattern as OutboxStorage.

### 3. SqlServerDeadLetterQueue Uses Hardcoded Table Name

**File**: `SqlServerDeadLetterQueue.cs`

`_tableName` is set but never used - SQL uses hardcoded "DeadLetterQueue".

**Fix**: Replace hardcoded strings with `{_tableName}`.

## Medium Priority Issues

1. **Missing ConfigureAwait(false)** - ~60+ async calls across multiple files
2. **Connection leaks in SqlServerQueueStorage** - Empty finally blocks
3. **Connection leaks in SqlServerInboxStorage** - Same pattern
4. **SqlServerMessageStorage missing AutoCreateTables check**
5. **Inconsistent schema validation in SqlServerInboxStorage**

## Compliant Areas

- TimeProvider usage: All classes use `_timeProvider.GetUtcNow()`
- No blocking calls: Zero `.Result`, `.Wait()`, `.GetAwaiter().GetResult()`
- Parameterized queries: All user data properly parameterized
- Double-check locking: Correct in most classes
- Transaction support with savepoints
- Optimistic concurrency in SagaRepository
