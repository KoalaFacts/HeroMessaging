# Git Worktrees Guide - HeroMessaging Development

**Created**: 2025-11-06
**Purpose**: Parallel development of production-readiness features

## Overview

This project uses **git worktrees** to enable parallel development of critical production features identified in the [Production Readiness Analysis](./production-readiness-analysis.md). Each worktree is a separate working directory with its own branch, allowing multiple features to be developed simultaneously without branch-switching overhead.

---

## Active Worktrees

### 1. **feature/idempotency-framework** 🔴 CRITICAL
**Location**: `../worktrees/feature-idempotency`
**Priority**: Critical (Blocker for production)
**Effort**: 2-3 weeks
**Owner**: TBD

**Scope**:
- [ ] Add `IIdempotencyChecker` interface to Abstractions
- [ ] Implement `InMemoryIdempotencyChecker` for development
- [ ] Implement `SqlServerIdempotencyChecker` for production
- [ ] Implement `PostgreSqlIdempotencyChecker` for production
- [ ] Create `IdempotencyDecorator` for automatic duplicate detection
- [ ] Add database migration scripts (SQL Server, PostgreSQL)
- [ ] Add cleanup background service with configurable retention
- [ ] Write 50+ unit tests
- [ ] Write 20+ integration tests with real databases
- [ ] Add performance benchmarks (<5ms overhead target)
- [ ] Document patterns and best practices
- [ ] Add code examples to README

**Key Files to Create**:
```
src/HeroMessaging.Abstractions/Processing/IIdempotencyChecker.cs
src/HeroMessaging/Processing/InMemoryIdempotencyChecker.cs
src/HeroMessaging/Processing/Decorators/IdempotencyDecorator.cs
src/HeroMessaging.Storage.SqlServer/SqlServerIdempotencyChecker.cs
src/HeroMessaging.Storage.PostgreSql/PostgreSqlIdempotencyChecker.cs
tests/HeroMessaging.Tests/Unit/IdempotencyCheckerTests.cs
tests/HeroMessaging.Tests/Integration/IdempotencyIntegrationTests.cs
tests/HeroMessaging.Benchmarks/IdempotencyBenchmarks.cs
docs/idempotency-guide.md
```

**Acceptance Criteria**:
- ✅ Duplicate messages detected and skipped automatically
- ✅ 80%+ code coverage with comprehensive tests
- ✅ <5ms overhead per message check
- ✅ Thread-safe under concurrent load
- ✅ Automatic cleanup of expired records
- ✅ Works with existing inbox/outbox patterns

---

### 2. **feature/rate-limiting** 🟠 HIGH PRIORITY
**Location**: `../worktrees/feature-rate-limiting`
**Priority**: High (Prevents resource exhaustion)
**Effort**: 1-2 weeks
**Owner**: TBD

**Scope**:
- [ ] Design `IRateLimiter` interface
- [ ] Implement token bucket algorithm (supports bursts)
- [ ] Implement sliding window algorithm (precise limiting)
- [ ] Implement concurrency limiter (bulkhead pattern)
- [ ] Create `RateLimitDecorator` for handlers
- [ ] Add builder configuration API
- [ ] Write 30+ unit tests
- [ ] Write 10+ integration tests
- [ ] Add performance benchmarks (<1ms overhead)
- [ ] Document tuning guide with use case recommendations

**Key Files to Create**:
```
src/HeroMessaging.Abstractions/Policies/IRateLimiter.cs
src/HeroMessaging/Policies/TokenBucketRateLimiter.cs
src/HeroMessaging/Policies/SlidingWindowRateLimiter.cs
src/HeroMessaging/Policies/ConcurrencyLimiter.cs
src/HeroMessaging/Processing/Decorators/RateLimitDecorator.cs
tests/HeroMessaging.Tests/Unit/RateLimiterTests.cs
tests/HeroMessaging.Benchmarks/RateLimitingBenchmarks.cs
docs/rate-limiting-guide.md
```

**Acceptance Criteria**:
- ✅ Token bucket supports configurable bursts
- ✅ Sliding window provides precise rate limits
- ✅ Concurrency limiter prevents overload
- ✅ <1ms overhead per rate check
- ✅ Observable via OpenTelemetry metrics
- ✅ Per-handler and global rate limits supported

---

### 3. **feature/batch-processing** 🟠 HIGH PRIORITY
**Location**: `../worktrees/feature-batch-processing`
**Priority**: High (10-100x throughput improvement)
**Effort**: 1-2 weeks
**Owner**: TBD

**Scope**:
- [ ] Design `IMessageBatcher` interface
- [ ] Implement batch publishing API
- [ ] Implement batch consumption API with configurable size/timeout
- [ ] Add transactional batch outbox writer
- [ ] Create `IBatchMessageHandler<T>` interface
- [ ] Add batch size and timeout configuration
- [ ] Write 25+ unit tests
- [ ] Write 15+ integration tests
- [ ] Add performance benchmarks (compare vs individual)
- [ ] Document batch processing patterns

**Key Files to Create**:
```
src/HeroMessaging.Abstractions/Processing/IMessageBatcher.cs
src/HeroMessaging.Abstractions/Handlers/IBatchMessageHandler.cs
src/HeroMessaging/Processing/BatchMessageConsumer.cs
src/HeroMessaging/Storage/BatchOutboxStorage.cs
src/HeroMessaging/Configuration/BatchingOptionsBuilder.cs
tests/HeroMessaging.Tests/Unit/BatchProcessingTests.cs
tests/HeroMessaging.Tests/Integration/BatchIntegrationTests.cs
tests/HeroMessaging.Benchmarks/BatchingBenchmarks.cs
docs/batch-processing-guide.md
```

**Acceptance Criteria**:
- ✅ Batch publishing is 10x+ faster than individual
- ✅ Configurable batch sizes (10-1000 messages)
- ✅ Timeout-based batch flushing (no starvation)
- ✅ Transaction safety maintained
- ✅ Partial failure handling (dead letter queue)
- ✅ Works with existing storage providers

---

### 4. **feature/deployment-artifacts** 🟡 MEDIUM PRIORITY
**Location**: `../worktrees/feature-deployment-artifacts`
**Priority**: Medium (Enables deployment)
**Effort**: 1 week
**Owner**: TBD

**Scope**:
- [ ] Create `Dockerfile` with multi-stage build
- [ ] Create `docker-compose.yml` for local development (RabbitMQ, PostgreSQL)
- [ ] Create `.dockerignore` file
- [ ] Create Kubernetes manifests (deployment, service, HPA, ingress)
- [ ] Create Helm chart (optional)
- [ ] Add deployment documentation
- [ ] Test on local Docker
- [ ] Test on local Kubernetes (k3s or minikube)

**Key Files to Create**:
```
Dockerfile
.dockerignore
docker-compose.yml
docker-compose.override.yml (local overrides)
kubernetes/deployment.yaml
kubernetes/service.yaml
kubernetes/configmap.yaml
kubernetes/secret.yaml (template)
kubernetes/hpa.yaml (horizontal pod autoscaler)
kubernetes/ingress.yaml
helm/ (optional Helm chart)
docs/deployment-guide.md
```

**Acceptance Criteria**:
- ✅ Multi-stage Dockerfile builds successfully
- ✅ docker-compose starts all dependencies (RabbitMQ, PostgreSQL, app)
- ✅ Kubernetes manifests deploy successfully to local cluster
- ✅ Health checks work in containerized environment
- ✅ Configuration via environment variables
- ✅ Documentation covers local and production deployment

---

## Working with Worktrees

### Navigate to a Worktree
```bash
# Go to idempotency worktree
cd ../worktrees/feature-idempotency

# Go to rate limiting worktree
cd ../worktrees/feature-rate-limiting

# Go to batch processing worktree
cd ../worktrees/feature-batch-processing

# Go to deployment artifacts worktree
cd ../worktrees/feature-deployment-artifacts

# Return to main worktree
cd c:/projects/BeingCiteable/HeroMessaging
```

### Check Worktree Status
```bash
# List all worktrees
git worktree list

# Check current branch in each worktree
git worktree list --porcelain
```

### Development Workflow

#### 1. Start Work on a Feature
```bash
# Navigate to feature worktree
cd ../worktrees/feature-idempotency

# Verify you're on the right branch
git branch --show-current
# Should show: feature/idempotency-framework

# Pull latest changes from main (if any)
git fetch origin
git merge origin/main

# Start coding!
```

#### 2. Commit Changes
```bash
# Stage changes
git add .

# Commit with descriptive message
git commit -m "Add IIdempotencyChecker interface

- Define core idempotency checking interface
- Add in-memory implementation for development
- Include unit tests for duplicate detection"

# Push to remote
git push -u origin feature/idempotency-framework
```

#### 3. Create Pull Request
```bash
# Use GitHub CLI (if installed)
gh pr create \
  --title "Feature: Idempotency Framework" \
  --body "$(cat <<'EOF'
## Summary
Implements idempotency checking to prevent duplicate message processing.

## Changes
- Added IIdempotencyChecker interface
- Implemented InMemoryIdempotencyChecker
- Added IdempotencyDecorator for automatic checking
- Comprehensive unit tests (50+)
- Integration tests with real databases (20+)
- Performance benchmarks (<5ms overhead verified)

## Testing
- All tests passing (158 → 228 tests)
- Code coverage maintained at 80%+
- Performance benchmarks show <5ms overhead

## Documentation
- Added docs/idempotency-guide.md
- Updated README with idempotency examples

Fixes #XX

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)" \
  --base main \
  --head feature/idempotency-framework
```

#### 4. Keep Worktree Updated
```bash
# While working, periodically sync with main
git fetch origin
git rebase origin/main

# Or merge if you prefer
git merge origin/main
```

#### 5. After PR is Merged
```bash
# Go back to main worktree
cd c:/projects/BeingCiteable/HeroMessaging

# Update main
git pull origin main

# Remove completed worktree
git worktree remove ../worktrees/feature-idempotency

# Delete remote branch (if desired)
git push origin --delete feature/idempotency-framework

# Delete local branch
git branch -d feature/idempotency-framework
```

---

## Parallel Development Tips

### Benefit 1: No Branch Switching
Work on multiple features simultaneously without `git checkout`:
```bash
# Terminal 1: Working on idempotency
cd ../worktrees/feature-idempotency
code .
# Edit, test, commit

# Terminal 2: Working on rate limiting (AT THE SAME TIME!)
cd ../worktrees/feature-rate-limiting
code .
# Edit, test, commit

# No need to commit/stash to switch contexts!
```

### Benefit 2: Compare Implementations
```bash
# Diff between two worktrees
diff -r ../worktrees/feature-idempotency/ ../worktrees/feature-rate-limiting/

# Or use a tool like Beyond Compare, Meld, etc.
```

### Benefit 3: Integration Testing Across Features
```bash
# Terminal 1: Run idempotency tests
cd ../worktrees/feature-idempotency
dotnet test --filter Category=Integration

# Terminal 2: Run rate limiting tests (simultaneously)
cd ../worktrees/feature-rate-limiting
dotnet test --filter Category=Integration

# No conflicts, no waiting!
```

---

## Best Practices

### 1. Keep Worktrees Small and Focused
- ✅ Each worktree = one feature/bug fix
- ❌ Don't create worktrees for exploratory work (use branches instead)

### 2. Sync Regularly
```bash
# At least daily, sync with main
git fetch origin
git rebase origin/main  # or merge if you prefer
```

### 3. Clean Up After Merge
```bash
# After PR is merged, remove worktree
git worktree remove ../worktrees/feature-name
```

### 4. Use Descriptive Branch Names
- ✅ `feature/idempotency-framework`
- ✅ `feature/rate-limiting`
- ✅ `fix/deadlock-in-saga-orchestrator`
- ❌ `temp-branch`
- ❌ `wip`
- ❌ `test`

### 5. Don't Share Worktrees
- Each developer should create their own worktrees
- Worktrees are local to your machine
- Only push branches to remote

---

## Troubleshooting

### Issue: "Worktree already exists"
```bash
# List existing worktrees
git worktree list

# If old worktree exists but directory is gone
git worktree prune

# Then retry
git worktree add ../worktrees/feature-name -b feature/branch-name main
```

### Issue: "Branch already exists"
```bash
# Delete the existing branch first
git branch -d feature/branch-name

# Or use -B to force creation
git worktree add ../worktrees/feature-name -B feature/branch-name main
```

### Issue: "Cannot remove worktree - changes not committed"
```bash
# Commit or stash changes first
cd ../worktrees/feature-name
git add .
git commit -m "WIP: Save progress"

# Then remove
cd c:/projects/BeingCiteable/HeroMessaging
git worktree remove ../worktrees/feature-name
```

### Issue: "Locked worktree"
```bash
# Unlock the worktree
git worktree unlock ../worktrees/feature-name

# Then remove
git worktree remove ../worktrees/feature-name
```

---

## Progress Tracking

### Phase 1: Critical Production Blockers (Current)

| Feature | Worktree | Branch | Status | Owner | ETA |
|---------|----------|--------|--------|-------|-----|
| Idempotency | `../worktrees/feature-idempotency` | `feature/idempotency-framework` | 🟡 In Progress | TBD | 2-3 weeks |
| Rate Limiting | `../worktrees/feature-rate-limiting` | `feature/rate-limiting` | 🟡 In Progress | TBD | 1-2 weeks |
| Batch Processing | `../worktrees/feature-batch-processing` | `feature/batch-processing` | 🟡 In Progress | TBD | 1-2 weeks |
| Deployment | `../worktrees/feature-deployment-artifacts` | `feature/deployment-artifacts` | 🟡 In Progress | TBD | 1 week |

**Target**: v1.0 Release - Minimum Viable Production

---

## Related Documents

- [Production Readiness Analysis](./production-readiness-analysis.md) - Full analysis report
- [CONTRIBUTING.md](../CONTRIBUTING.md) - Contribution guidelines
- [CLAUDE.md](../CLAUDE.md) - Development guidelines for AI assistants
- [README.md](../README.md) - Project overview

---

## Questions?

For questions about worktrees or development workflow:
- Create an issue: https://github.com/KoalaFacts/HeroMessaging/issues
- Start a discussion: https://github.com/KoalaFacts/HeroMessaging/discussions
- Review git worktree docs: https://git-scm.com/docs/git-worktree

---

**Created**: 2025-11-06
**Last Updated**: 2025-11-06
**Maintained By**: Development Team
