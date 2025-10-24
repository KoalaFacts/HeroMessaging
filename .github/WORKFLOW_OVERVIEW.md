# GitHub Workflows Overview

Complete documentation of all CI/CD workflows for the HeroMessaging project.

---

## 📋 Workflow Inventory

| Workflow | File | Purpose | Triggers |
|----------|------|---------|----------|
| **Coverage Report** | `coverage.yml` | Code coverage enforcement (80% threshold) | Push (main, develop), PR (main) |
| **Tests** | `test.yml` | Quick cross-platform validation | Push (main, develop), PR (main, develop) |
| **CI** | `ci.yml` | Comprehensive testing with change detection | Push (all branches), PR, Schedule, Manual |
| **Performance Benchmarks** | `performance.yml` | Performance testing and benchmarking | Push (main), PR (main), Schedule |
| **Publish to NuGet** | `publish-nuget.yml` | Package publishing via trusted publishing | Release, Manual |

---

## 🔄 End-to-End Development Flow

### Development Workflow (Feature Branch → Main)

```
┌─────────────────────────────────────────────────────────────────────┐
│ Developer pushes to feature branch (e.g., feature/new-feature)      │
└─────────────────────────────────────────────────────────────────────┘
                                ↓
┌─────────────────────────────────────────────────────────────────────┐
│ WORKFLOW: CI (ci.yml)              │
│ ├─ detect-changes: Analyze what files changed                       │
│ ├─ unit-tests: 3 OS × 3 .NET versions × 2 configs = 18 jobs        │
│ │  └─ Runs on: ubuntu, windows, macos                              │
│ ├─ integration-tests: Database tests (Linux only, 4 categories)    │
│ │  └─ Services: PostgreSQL, SQL Server, Redis                      │
│ ├─ contract-tests: API compatibility tests                         │
│ └─ quality-gates: Combined coverage report (80% threshold)          │
└─────────────────────────────────────────────────────────────────────┘
                                ↓
┌─────────────────────────────────────────────────────────────────────┐
│ Developer creates Pull Request to main                              │
└─────────────────────────────────────────────────────────────────────┘
                                ↓
┌─────────────────────────────────────────────────────────────────────┐
│ ALL PR WORKFLOWS RUN IN PARALLEL:                                   │
│                                                                      │
│ ┌───────────────────────────────────────────────────────────────┐  │
│ │ 1. Tests (test.yml)                                           │  │
│ │    └─ Cross-platform quick validation (9 jobs)               │  │
│ └───────────────────────────────────────────────────────────────┘  │
│                                                                      │
│ ┌───────────────────────────────────────────────────────────────┐  │
│ │ 2. Coverage Report (coverage.yml)                             │  │
│ │    └─ Generate coverage report + 80% threshold check          │  │
│ └───────────────────────────────────────────────────────────────┘  │
│                                                                      │
│ ┌───────────────────────────────────────────────────────────────┐  │
│ │ 3. CI (ci.yml)               │  │
│ │    └─ Full test suite with change detection                   │  │
│ └───────────────────────────────────────────────────────────────┘  │
│                                                                      │
│ ┌───────────────────────────────────────────────────────────────┐  │
│ │ 4. Performance Benchmarks (performance.yml) - main PRs only   │  │
│ │    └─ Run benchmarks + post results comment                   │  │
│ └───────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────┘
                                ↓
┌─────────────────────────────────────────────────────────────────────┐
│ All checks pass ✅                                                   │
│ Reviewer approves PR                                                 │
│ PR merged to main                                                    │
└─────────────────────────────────────────────────────────────────────┘
                                ↓
┌─────────────────────────────────────────────────────────────────────┐
│ Push to main triggers workflows:                                    │
│ ├─ Tests (test.yml)                                                 │
│ ├─ Coverage Report (coverage.yml)                                   │
│ ├─ CI (ci.yml)                     │
│ │  └─ Stores performance baseline for regression detection          │
│ └─ Performance Benchmarks (performance.yml)                         │
│    └─ Stores benchmark results as artifacts                         │
└─────────────────────────────────────────────────────────────────────┘
```

---

### Release Workflow (Main → NuGet.org)

```
┌─────────────────────────────────────────────────────────────────────┐
│ Maintainer decides to release version 1.0.0                         │
└─────────────────────────────────────────────────────────────────────┘
                                ↓
┌─────────────────────────────────────────────────────────────────────┐
│ Create Git tag and GitHub Release:                                  │
│ $ git tag v1.0.0                                                     │
│ $ git push origin v1.0.0                                             │
│ Then create release at: github.com/.../releases/new                 │
└─────────────────────────────────────────────────────────────────────┘
                                ↓
┌─────────────────────────────────────────────────────────────────────┐
│ WORKFLOW: Publish to NuGet (publish-nuget.yml)                      │
│                                                                      │
│ ┌───────────────────────────────────────────────────────────────┐  │
│ │ Job 1: validate                                               │  │
│ │ ├─ Extract version from tag (v1.0.0 → 1.0.0)                 │  │
│ │ ├─ Restore dependencies                                       │  │
│ │ ├─ Build solution                                             │  │
│ │ └─ Run unit tests (must pass!)                               │  │
│ └───────────────────────────────────────────────────────────────┘  │
│                                ↓                                     │
│ ┌───────────────────────────────────────────────────────────────┐  │
│ │ Job 2: build-packages                                         │  │
│ │ ├─ Build solution                                             │  │
│ │ ├─ Pack HeroMessaging.nupkg (+ symbols)                       │  │
│ │ ├─ Pack HeroMessaging.Abstractions.nupkg (+ symbols)          │  │
│ │ └─ Upload artifacts                                           │  │
│ └───────────────────────────────────────────────────────────────┘  │
│                                ↓                                     │
│ ┌───────────────────────────────────────────────────────────────┐  │
│ │ Job 3: publish-nuget (Environment: production)                │  │
│ │ ├─ Download packages                                          │  │
│ │ ├─ Authenticate via OIDC (NuGet Trusted Publishing)           │  │
│ │ └─ Push to NuGet.org (no API key needed!)                     │  │
│ └───────────────────────────────────────────────────────────────┘  │
│                                ↓                                     │
│ ┌───────────────────────────────────────────────────────────────┐  │
│ │ Job 4: publish-github-packages                                │  │
│ │ └─ Push to GitHub Packages                                    │  │
│ └───────────────────────────────────────────────────────────────┘  │
│                                ↓                                     │
│ ┌───────────────────────────────────────────────────────────────┐  │
│ │ Job 5: create-release-notes                                   │  │
│ │ └─ Update GitHub Release with NuGet links                     │  │
│ └───────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────┘
                                ↓
┌─────────────────────────────────────────────────────────────────────┐
│ ✅ Package published to:                                             │
│ ├─ https://www.nuget.org/packages/HeroMessaging/1.0.0              │
│ ├─ https://www.nuget.org/packages/HeroMessaging.Abstractions/1.0.0 │
│ └─ https://github.com/KoalaFacts/HeroMessaging/packages            │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 🌙 Scheduled Workflows (Nightly)

Every day at 2:00 AM UTC:

```
┌─────────────────────────────────────────────────────────────────────┐
│ Scheduled Trigger (cron: '0 2 * * *')                               │
└─────────────────────────────────────────────────────────────────────┘
                                ↓
┌─────────────────────────────────────────────────────────────────────┐
│ Run in parallel:                                                     │
│ ├─ CI (with performance tests enabled)      │
│ │  └─ Full matrix + performance regression detection                │
│ └─ Performance Benchmarks                                           │
│    └─ Detailed benchmark runs                                       │
└─────────────────────────────────────────────────────────────────────┘
```

**Purpose**:
- Catch regressions that develop over time
- Test on latest runner images and dependencies
- Update performance baselines
- Comprehensive testing without PR pressure

---

## 📊 Workflow Details

### 1. Coverage Report (coverage.yml)

**Runtime**: ~3-5 minutes
**Timeout**: 15 minutes

**Jobs**:
- `coverage`: Single job on ubuntu-latest
  - Runs all tests with coverage collection
  - Generates HTML + badge + text summary
  - Enforces 80% coverage threshold
  - Uploads coverage artifacts

**Success Criteria**:
- ✅ All tests pass
- ✅ Coverage ≥ 80%

---

### 2. Tests (test.yml)

**Runtime**: ~5-15 minutes per job (9 jobs total)
**Timeout**: 20 minutes per job

**Jobs**:
- `test`: Matrix of 9 combinations
  - OS: ubuntu-latest, windows-latest, macos-latest (3)
  - .NET: 6.0.x, 8.0.x, 10.0.x (3)
  - Runs: Unit, Integration, Contract tests sequentially

**Success Criteria**:
- ✅ All tests pass on all platforms

**Use Case**: Quick smoke test for PRs

---

### 3. CI (ci.yml)

**Runtime**: ~15-45 minutes total
**Timeout**: Various per job

**Jobs** (Sequential with dependencies):

#### Job 1: `detect-changes`
- Analyzes file changes
- Determines which tests to run
- Skips workflows if only docs changed

#### Job 2: `unit-tests` (depends on detect-changes)
- Matrix: 3 OS × 3 .NET versions × 2 configs
- Excludes some combos to optimize (e.g., macOS Debug)
- Caches NuGet packages
- Uploads coverage to Codecov (ubuntu-latest, net8.0, Release only)

#### Job 3: `integration-tests` (depends on detect-changes, unit-tests)
- **Linux only** (service containers)
- 4 test categories: Storage, Serialization, Observability, Pipeline
- Service containers: PostgreSQL 15, SQL Server 2022, Redis 7
- Uses connection strings from environment

#### Job 4: `contract-tests` (depends on detect-changes)
- API compatibility tests
- Runs when core or tests changed

#### Job 5: `performance-regression` (depends on detect-changes, unit-tests)
- Downloads baseline benchmarks
- Runs current benchmarks
- Analyzes for >10% regression
- Uploads new baseline (main branch only)
- Comments regression report on PRs

#### Job 6: `quality-gates` (depends on unit-tests, integration-tests, contract-tests)
- Downloads all test results
- Generates combined coverage report
- Enforces 80% coverage threshold
- Checks all test jobs succeeded

**Success Criteria**:
- ✅ All job dependencies succeeded
- ✅ Combined coverage ≥ 80%
- ✅ No performance regression >10%

**Use Case**: Comprehensive validation before merge

---

### 4. Performance Benchmarks (performance.yml)

**Runtime**: ~10-30 minutes
**Timeout**: 45 minutes

**Jobs**:
- `benchmark`: Single job on ubuntu-latest
  - Runs BenchmarkDotNet suite
  - Exports JSON + HTML results
  - Comments on PR with results link

**Success Criteria**:
- ✅ Benchmarks complete successfully

**Use Case**:
- Track performance over time
- Identify performance regressions
- Generate detailed metrics

---

### 5. Publish to NuGet (publish-nuget.yml)

**Runtime**: ~5-10 minutes
**Timeout**: 15 minutes per job

**Jobs** (Sequential):

#### Job 1: `validate`
- Extract version from tag or input
- Run unit tests (must pass)
- Output: version, is-prerelease

#### Job 2: `build-packages` (depends on validate)
- Pack both packages with version
- Include symbol packages (.snupkg)
- Upload as artifacts (30 day retention)

#### Job 3: `publish-nuget` (depends on validate, build-packages)
- **Environment**: `production` (protection rules apply)
- Authenticate via OIDC trusted publishing
- Push to NuGet.org
- **No API key required!**

#### Job 4: `publish-github-packages` (depends on validate, build-packages)
- Push to GitHub Packages
- Uses GITHUB_TOKEN

#### Job 5: `create-release-notes` (depends on publish-nuget, publish-github-packages)
- Only on release events
- Updates GitHub release with package links
- Adds installation instructions

**Success Criteria**:
- ✅ Unit tests pass
- ✅ Packages build successfully
- ✅ NuGet.org trusted publisher configured
- ✅ Published to NuGet.org and GitHub Packages

**Use Case**: Production package releases

---

## 🔍 End-to-End Flow Analysis

### Scenario 1: Feature Development

```
1. Create feature branch → Pushes trigger: ci.yml
2. Create PR to main → Triggers: test.yml, coverage.yml, ci.yml, performance.yml
3. All workflows pass → Merge approved
4. Merge to main → Triggers: All test workflows again + stores baselines
```

**Coverage**: ✅ Complete
**Gaps**: None
**Optimization**: Consider consolidating test.yml and ci.yml

---

### Scenario 2: Hotfix to Main

```
1. Create hotfix branch from main
2. Make minimal change
3. Create PR → All test workflows run
4. Fast-track review
5. Merge to main → Full validation again
```

**Coverage**: ✅ Complete
**Gaps**: None
**Note**: Could add branch protection requiring status checks

---

### Scenario 3: Release Production Package

```
1. All tests passing on main ✅
2. Create tag: v1.0.0
3. Create GitHub Release
4. publish-nuget.yml triggers
5. Tests run again as validation
6. Packages published to NuGet.org + GitHub
7. Release notes auto-updated
```

**Coverage**: ✅ Complete
**Gaps**: None
**Security**: ✅ OIDC trusted publishing, environment protection

---

### Scenario 4: Nightly Regression Detection

```
1. Cron triggers at 2 AM UTC
2. ci.yml runs full suite + performance tests
3. performance.yml runs detailed benchmarks
4. Baselines updated if on main branch
5. Team notified of failures via GitHub notifications
```

**Coverage**: ✅ Complete
**Gaps**: Could add Slack/Discord notifications for failures

---

## ✅ Workflow Integration Checklist

### Code Quality Gates
- ✅ Unit tests (all workflows)
- ✅ Integration tests (ci.yml)
- ✅ Contract tests (ci.yml)
- ✅ Coverage threshold 80% (coverage.yml, ci.yml)
- ✅ Cross-platform testing (test.yml, ci.yml)
- ✅ Multi-framework testing (.NET 6, 8, 10)

### Performance Monitoring
- ✅ Performance benchmarks (performance.yml)
- ✅ Regression detection (ci.yml)
- ✅ Baseline tracking (ci.yml)
- ✅ PR performance comments (performance.yml)

### Security
- ✅ Explicit permissions on all workflows
- ✅ NuGet trusted publishing (no API keys)
- ✅ Environment protection (production)
- ✅ Concurrency controls (prevent race conditions)
- ✅ Dependabot for action updates

### Release Management
- ✅ Automated package building
- ✅ Multi-target publishing (NuGet.org + GitHub)
- ✅ Version validation
- ✅ Release notes automation
- ✅ Symbol package publishing

### Developer Experience
- ✅ Fast feedback (test.yml runs quickly)
- ✅ Change detection (skip unnecessary tests)
- ✅ Detailed logs and artifacts
- ✅ PR comments with results
- ✅ Manual workflow triggers

---

## 🚨 Identified Gaps and Recommendations

### Gap 1: Workflow Duplication
**Issue**: `test.yml` and `ci.yml` have overlapping functionality

**Impact**: Medium - Extra CI minutes consumed

**Recommendation**:
- **Option A**: Remove `test.yml`, rely solely on `ci.yml`
- **Option B**: Make `test.yml` a quick smoke test (single OS, single .NET version)
- **Option C**: Keep both but clarify purposes in names

**Status**: ⚠️ Consider optimization

---

### Gap 2: Integration Test Secrets
**Issue**: Database passwords currently hardcoded (documented in SECRETS_MIGRATION.md)

**Impact**: High - Security risk

**Recommendation**:
1. Add secrets to GitHub repository settings
2. Update ci.yml to use secrets
3. See `.github/SECRETS_MIGRATION.md` for instructions

**Status**: ⚠️ Action required

---

### Gap 3: No CodeQL Security Scanning
**Issue**: No automated security vulnerability scanning

**Impact**: Medium - May miss security issues

**Recommendation**: Add CodeQL workflow:
```yaml
name: CodeQL
on: [push, pull_request]
jobs:
  analyze:
    runs-on: ubuntu-latest
    permissions:
      security-events: write
    steps:
      - uses: actions/checkout@v4
      - uses: github/codeql-action/init@v3
        with:
          languages: csharp
      - uses: github/codeql-action/analyze@v3
```

**Status**: 💡 Nice to have

---

### Gap 4: No Workflow Status Badge
**Issue**: No README badge showing workflow status

**Impact**: Low - Visibility issue

**Recommendation**: Add to README.md:
```markdown
[![Tests](https://github.com/KoalaFacts/HeroMessaging/workflows/Tests/badge.svg)](https://github.com/KoalaFacts/HeroMessaging/actions)
[![Coverage](https://codecov.io/gh/KoalaFacts/HeroMessaging/branch/main/graph/badge.svg)](https://codecov.io/gh/KoalaFacts/HeroMessaging)
```

**Status**: 💡 Nice to have

---

### Gap 5: No Pre-commit Hooks
**Issue**: Developers can commit code that fails CI

**Impact**: Medium - Wastes CI time and creates friction

**Recommendation**: Add Husky or pre-commit hooks:
- Run dotnet format before commit
- Run unit tests before push
- Check for secrets/sensitive data

**Status**: 💡 Nice to have

---

## 📈 Performance Metrics

### Expected CI Runtime

| Event | Workflows Triggered | Parallel Jobs | Est. Runtime | CI Cost |
|-------|---------------------|---------------|--------------|---------|
| **Push to feature branch** | ci.yml | 18-26 jobs | 15-25 min | Medium |
| **PR to main** | All 4 test workflows | 30-40 jobs | 20-30 min | High |
| **Merge to main** | All 4 test workflows | 30-40 jobs | 20-30 min | High |
| **Nightly schedule** | ci, performance | 20-30 jobs | 30-45 min | Medium |
| **Release publish** | publish-nuget.yml | 5 jobs (sequential) | 5-10 min | Low |

**Total estimated monthly CI minutes** (assuming 50 PRs/month + nightly):
- PRs: 50 × 25 min = 1,250 min
- Nightly: 30 × 40 min = 1,200 min
- Releases: 4 × 10 min = 40 min
- **Total**: ~2,500 minutes/month

**GitHub Free Tier**: 2,000 minutes/month
**Overage Cost**: ~500 min × $0.008/min = $4/month

---

## 🎯 Summary

### Strengths ✅
1. **Comprehensive testing**: Unit, integration, contract, performance
2. **Security best practices**: Permissions, trusted publishing, no secrets
3. **Automated publishing**: NuGet + GitHub Packages with validation
4. **Change detection**: Smart skipping of unnecessary tests
5. **Cross-platform**: Tests on Windows, Linux, macOS
6. **Multi-framework**: .NET 6, 8, 10 coverage

### Areas for Improvement ⚠️
1. **Workflow consolidation**: test.yml and ci.yml overlap
2. **Secrets migration**: Move hardcoded passwords to GitHub Secrets
3. **Security scanning**: Add CodeQL for vulnerability detection
4. **Pre-commit hooks**: Catch issues before CI

### Overall Assessment 🎉
**Grade: A-**

The workflow setup is production-ready with excellent security practices,
comprehensive testing, and modern CI/CD patterns. The minor gaps identified
are optimizations rather than critical issues.

---

**Last Updated**: 2025-10-24
**Maintained By**: Development Team
**Questions**: See `.github/NUGET_TRUSTED_PUBLISHING.md` or `.github/SECRETS_MIGRATION.md`
