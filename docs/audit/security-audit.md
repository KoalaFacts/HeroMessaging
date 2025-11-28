# HeroMessaging.Security Code Quality Audit Report

**Audit Date**: 2025-11-28
**Overall Risk Level**: Medium

## Summary

| Metric | Value |
|--------|-------|
| Critical Issues | 2 |
| High Priority Issues | 3 |
| Medium Priority Issues | 5 |
| Low Priority Issues | 3 |

## Critical Issues

### 1. TimeProvider Not Used in AesGcmMessageEncryptor

**File**: `Encryption/AesGcmMessageEncryptor.cs`

Constructor does not accept TimeProvider - inconsistent with other security components.

**Fix**: Add `TimeProvider` parameter to constructor.

### 2. Hardcoded DateTimeOffset.UtcNow in MessageSignature

**File**: `HeroMessaging.Abstractions/Security/IMessageSigner.cs:105`

```csharp
Timestamp = timestamp ?? DateTimeOffset.UtcNow;  // HARDCODED!
```

**Fix**: Require timestamp parameter or use TimeProvider.

## High Priority Issues

### 1. No Key Zeroing on Disposal

**Files**: `AesGcmMessageEncryptor.cs`, `HmacSha256MessageSigner.cs`

Keys remain in memory after use.

**Fix**: Implement `IDisposable` with `CryptographicOperations.ZeroMemory(_key)`.

### 2. Thread Safety in Authentication/Authorization Providers

**Files**: `ClaimsAuthenticationProvider.cs`, `PolicyAuthorizationProvider.cs`

Regular `Dictionary<>` used - not thread-safe for runtime registration.

**Fix**: Use `ConcurrentDictionary<>`.

### 3. API Keys Stored Unhashed

**File**: `ClaimsAuthenticationProvider.cs`

API keys stored as plain strings in dictionary.

**Fix**: Store hashed API keys, compare hashes.

## Positive Observations

- Constant-time comparison using `CryptographicOperations.FixedTimeEquals()`
- Proper AES-GCM usage (96-bit nonce, 128-bit tag, 256-bit key)
- Key validation in constructors
- Keys copied to internal arrays (not stored by reference)
- TimeProvider used in HmacSha256MessageSigner
- No hardcoded keys or secrets
- No blocking async calls
- Zero-allocation Span APIs
- Secure random number generation (CSPRNG)
