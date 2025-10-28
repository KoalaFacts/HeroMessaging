# Pending: Integration Tests Workflow Separation

## Status
The integration tests workflow separation is complete but **cannot be pushed** due to GitHub App permissions.

## Issue
GitHub App lacks `workflows` permission to modify files in `.github/workflows/`.

Error message:
```
refusing to allow a GitHub App to create or update workflow `.github/workflows/ci.yml`
without `workflows` permission
```

## Solution Required

### Grant Workflows Permission to GitHub App

**Option 1: GitHub App Installation Settings**
1. Visit: https://github.com/settings/installations
2. Find the Claude Code GitHub App
3. Click **Configure**
4. Scroll to **Repository permissions**
5. Find **Workflows** → Change to **Read and write**
6. Click **Save**

**Option 2: Manual Push**
The changes are stashed in git. You can apply and push them manually:

```bash
# Apply the stashed workflow changes
git stash pop

# Review the changes
git status
git diff .github/workflows/

# Commit and push
git add .github/workflows/
git commit -m "feat: Separate integration tests into dedicated workflow"
git push origin claude/session-011CUZNxDEGUuuaJysjQiNbf
```

## Changes Made

### New File: `.github/workflows/integration-tests.yml`
- Runs on every commit (all branches)
- Runs nightly on main branch at 3 AM UTC
- Includes PostgreSQL and SQL Server services
- Same configuration as previous integration-tests job

### Modified: `.github/workflows/ci.yml`
- Removed integration-tests job (lines 112-242)
- Updated quality-gates dependency from `[unit-tests, integration-tests, contract-tests]` to `[unit-tests, contract-tests]`
- Removed `run_integration_tests` workflow_dispatch input
- Updated quality gate checks to remove integration test validation
- Added informational message: "Integration tests run in separate workflow"

## Benefits

1. **Faster Feedback**: Unit tests no longer wait for integration tests
2. **Independent Resources**: Integration tests have dedicated CI resources
3. **Clearer Separation**: Test types are organizationally distinct
4. **Flexible Scheduling**: Integration tests can run on different schedules

## Next Steps

1. Grant workflows permission to the GitHub App (see Solution Required above)
2. Apply the stashed changes: `git stash pop`
3. Push the changes to the repository

---

**Created**: 2025-10-28
**Branch**: claude/session-011CUZNxDEGUuuaJysjQiNbf
**Stash**: `git stash list` should show the workflow changes
