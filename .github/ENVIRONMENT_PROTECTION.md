# Environment Protection Setup Guide

This guide explains how to configure environment protection rules for the HeroMessaging repository, ensuring safe deployments with manual approval gates.

## Overview

The `publish-nuget.yml` workflow uses a **production** environment that should be protected with manual approval before publishing packages to NuGet.org. This prevents accidental or unauthorized releases.

## Quick Setup

### Prerequisites

- Repository admin access
- GitHub Pro, Team, or Enterprise (for environment protection on private repos)
- Note: Public repos get environment protection for free

### Step-by-Step Configuration

1. **Navigate to Environment Settings**
   ```
   Repository → Settings → Environments → New environment
   ```

2. **Create Production Environment**
   - Name: `production`
   - Click "Configure environment"

3. **Enable Required Reviewers**
   - ✅ Check "Required reviewers"
   - Add reviewers (team or individual users):
     - Option 1: Add maintainer team (e.g., `@KoalaFacts/maintainers`)
     - Option 2: Add individual maintainers by username
   - Minimum: 1 required approval (recommended: 2 for critical packages)

4. **Configure Wait Timer (Optional)**
   - Set deployment delay: 0 minutes (approval is the gate)
   - Or add 5-10 minute wait for last-minute review

5. **Set Deployment Branches**
   - ✅ "Selected branches"
   - Add allowed branches:
     - `main` (releases only from main)
     - Pattern: `release/*` (if using release branches)

6. **Environment Secrets (Optional)**
   - These are only available when deploying to this environment
   - Can store sensitive tokens here for extra security

7. **Save Configuration**
   - Click "Save protection rules"

## Environment Configuration Details

### Production Environment

```yaml
# In publish-nuget.yml (already configured)
environment:
  name: production
  url: https://www.nuget.org/packages/HeroMessaging
```

**Protection Rules:**
- ✅ Required reviewers: 1-2 maintainers
- ✅ Deployment branches: `main` only
- ✅ Wait timer: Optional (0-10 minutes)
- ✅ Environment secrets: Optional

### Staging Environment (Future)

For testing releases before production:

```yaml
environment:
  name: staging
  url: https://github.com/KoalaFacts/HeroMessaging/packages
```

**Protection Rules:**
- ✅ Required reviewers: 1 reviewer
- ✅ Deployment branches: `main`, `develop`, `release/*`
- ✅ Auto-publish to GitHub Packages for testing

## Approval Workflow

### How Manual Approval Works

1. **Release Triggered**
   - User creates a release via `create-release` workflow
   - `publish-nuget` workflow automatically starts
   - Workflow runs up to the `production` environment step

2. **Approval Request**
   - Workflow pauses at production environment
   - GitHub sends notifications to required reviewers:
     - Email notification
     - GitHub notifications
     - In-app notification

3. **Review Process**
   - Reviewers receive deployment request
   - Review includes:
     - Package version
     - Release notes
     - CI test results
     - Security scan results
   - Reviewers can:
     - ✅ Approve (deployment proceeds)
     - ❌ Reject (deployment cancelled)
     - 💬 Comment (request changes)

4. **Deployment**
   - After approval: Packages published to NuGet.org
   - After rejection: Workflow cancelled, no publication

### Approval Notification Example

```
📦 Deployment Awaiting Approval

Environment: production
Workflow: Publish to NuGet
Version: 1.2.3
Requested by: @username

Review the release:
- https://github.com/KoalaFacts/HeroMessaging/releases/tag/v1.2.3

Approve or reject this deployment:
- [Approve] [Reject] [View workflow]
```

## Best Practices

### Reviewer Selection

✅ **Do:**
- Use a team for reviewer assignments (easier management)
- Require at least 1 reviewer for production
- Consider 2 reviewers for major version releases
- Include maintainers with publishing authority

❌ **Don't:**
- Allow workflow author to self-approve (enable "Prevent self-review")
- Use personal accounts only (team is more resilient)
- Skip reviews for "quick fixes" (defeats the purpose)

### Review Checklist

Before approving a deployment, verify:

- [ ] **Version**: Correct semantic version (major.minor.patch)
- [ ] **Tests**: All CI tests passed (unit + integration)
- [ ] **Security**: No security vulnerabilities detected
- [ ] **Coverage**: Meets 80% coverage threshold
- [ ] **Release Notes**: Changelog is accurate and complete
- [ ] **Breaking Changes**: Documented if major version bump
- [ ] **SBOM**: Software Bill of Materials generated
- [ ] **Signing**: Packages signed (if certificate configured)

### Deployment Timing

- **Business Hours**: Schedule releases during work hours for support
- **Avoid Fridays**: Don't deploy late week (harder to fix issues)
- **Coordinate**: Notify team before approving major releases
- **Monitor**: Watch NuGet.org after approval for successful publication

## Verification

### Test the Approval Flow

1. **Trigger a Test Release**
   ```bash
   # Create a test release (draft mode)
   gh workflow run create-release.yml \
     -f version=0.0.1-test.1 \
     -f draft=true
   ```

2. **Check Approval Request**
   - Go to Actions → Publish to NuGet workflow
   - Verify it stops at "production" environment
   - Check if reviewers receive notifications

3. **Approve or Reject**
   - Click "Review deployments"
   - Test approval flow
   - Verify workflow proceeds after approval

4. **Clean Up**
   - Delete test release
   - Delete test packages from NuGet (if needed)

### Verify Environment Configuration

```bash
# Using GitHub CLI
gh api repos/KoalaFacts/HeroMessaging/environments/production | jq '.protection_rules'
```

Expected output:
```json
[
  {
    "type": "required_reviewers",
    "reviewers": [
      {
        "type": "User",
        "reviewer": {
          "login": "username"
        }
      }
    ]
  }
]
```

## Troubleshooting

### Common Issues

**Issue: Environment not found**
```
Error: The environment 'production' was not found
```
**Solution**: Create the environment in Settings → Environments

---

**Issue: No approval requested**
```
Workflow runs without waiting for approval
```
**Solution**:
- Check environment protection rules are enabled
- Verify "Required reviewers" is checked
- Ensure deployment branch matches

---

**Issue: Wrong reviewers notified**
```
Unexpected users receiving approval requests
```
**Solution**:
- Update reviewer list in environment settings
- Check team membership if using team reviewers
- Verify reviewer permissions

---

**Issue: Can't approve own deployment**
```
Self-approval blocked
```
**Solution**: This is intended behavior for security
- Request approval from another maintainer
- Or temporarily disable "Prevent self-review" (not recommended)

## Advanced Configuration

### Multiple Environments

Create a deployment pipeline:

```
┌──────────────┐
│   Staging    │ ← Auto-deploy on release (GitHub Packages)
│  (no approval)│
└──────┬───────┘
       │ (verify)
       ▼
┌──────────────┐
│  Production  │ ← Manual approval required (NuGet.org)
│ (1+ reviewers)│
└──────────────┘
```

### Conditional Approvals

Require approval only for specific versions:

```yaml
environment:
  name: ${{ github.event.release.prerelease && 'staging' || 'production' }}
```

- Pre-releases → staging (auto-deploy)
- Stable releases → production (manual approval)

### Approval Timeouts

Set maximum time before deployment expires:

- Settings → Environments → production
- "Wait timer": 60 minutes
- Deployment auto-rejects after timeout

## Security Considerations

### Why Manual Approval?

**Prevents:**
- ❌ Accidental releases (typo in version, wrong branch)
- ❌ Malicious releases (compromised account/token)
- ❌ Untested code (CI passed but manual testing reveals issues)
- ❌ Breaking changes (major version without proper review)

**Provides:**
- ✅ Human oversight before public release
- ✅ Final sanity check of version and changes
- ✅ Audit trail of who approved what
- ✅ Compliance with release procedures

### Audit Trail

All approvals are logged:

- Who approved/rejected
- When the decision was made
- Comments provided
- Workflow run ID

Access audit logs:
```
Repository → Settings → Environments → production → Deployment history
```

## Additional Resources

- [GitHub Environments Documentation](https://docs.github.com/en/actions/deployment/targeting-different-environments/using-environments-for-deployment)
- [Required Reviewers](https://docs.github.com/en/actions/deployment/targeting-different-environments/using-environments-for-deployment#required-reviewers)
- [Deployment Branches](https://docs.github.com/en/actions/deployment/targeting-different-environments/using-environments-for-deployment#deployment-branches)

## Summary

✅ **Configured**: `publish-nuget.yml` uses production environment
⚙️ **Required**: Repository admin must enable protection rules
🔐 **Benefit**: Manual approval prevents accidental NuGet releases
📝 **Compliance**: Audit trail for all production deployments

---

**Last Updated**: 2025-11-07
**Version**: 1.0
**Applies To**: publish-nuget.yml workflow
