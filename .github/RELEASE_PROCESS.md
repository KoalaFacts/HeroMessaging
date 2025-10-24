# Release Process Guide

This document describes how to create and publish releases for HeroMessaging using the automated GitHub workflows.

---

## 📋 Overview

The release process uses a 3-stage pipeline:

1. **CI Workflow** (`test-matrix.yml`) - Tests pass on main → automatically builds and stores packages
2. **Create Release** (`create-release.yml`) - Downloads pre-built packages → creates GitHub release
3. **Publish to NuGet** (`publish-nuget.yml`) - Downloads from release assets → publishes to NuGet.org

This approach provides:
- ✅ **No duplicate builds** - Packages built once by CI, reused for releases
- ✅ **No duplicate tests** - Release uses packages that already passed all tests
- ✅ **Fast releases** - No build/test time, just package downloads
- ✅ **Review before publish** - Draft releases can be reviewed before going live
- ✅ **Audit trail** - Release links back to exact CI run that built the packages
- ✅ **Failure recovery** - Can retry at any stage without rebuilding

---

## 🚀 Quick Start

### Standard Release

**Prerequisites**: Latest commit on `main` has passed all CI tests

```bash
# 1. Go to Actions → Create Release
# 2. Click "Run workflow"
# 3. Enter version: 1.0.0
# 4. Leave "commit SHA" empty (uses latest main)
# 5. Leave "prerelease" and "draft" unchecked
# 6. Click "Run workflow"

# The workflow will:
# - Validate version format
# - Find successful CI workflow run for latest main commit
# - Download pre-built packages from CI artifacts
# - Rename packages with release version
# - Create GitHub release with tag v1.0.0
# - Attach packages as release assets
# - Auto-trigger publish-nuget workflow
# - Publish to NuGet.org and GitHub Packages
```

**Result**: Version 1.0.0 published to NuGet.org in ~5-8 minutes (no build/test time!)

---

## 📖 Detailed Process

### Step 1: Create the Release

#### Via GitHub UI

1. Navigate to: https://github.com/KoalaFacts/HeroMessaging/actions/workflows/create-release.yml

2. Click **"Run workflow"** dropdown

3. Fill in the form:
   - **Branch**: `main` (recommended) or your release branch
   - **Release version**: e.g., `1.0.0`, `1.1.0-beta.1`, `2.0.0-rc.1`
   - **Mark as pre-release**: Check if this is a beta/RC/alpha release
   - **Create as draft release**: Check to review before publishing

4. Click **"Run workflow"**

#### Via GitHub CLI

```bash
# Standard release
gh workflow run create-release.yml \
  -f version=1.0.0 \
  -f prerelease=false \
  -f draft=false

# Pre-release (beta)
gh workflow run create-release.yml \
  -f version=1.1.0-beta.1 \
  -f prerelease=true \
  -f draft=false

# Draft release (for review)
gh workflow run create-release.yml \
  -f version=2.0.0 \
  -f prerelease=false \
  -f draft=true
```

---

### Step 2: What Happens Automatically

#### STAGE 1: CI builds packages (happens on every main push)

```
┌─────────────────────────────────────────┐
│ test-matrix.yml (on main branch push)   │
│ ├─ Run all tests (unit, integration, etc)│
│ ├─ Quality gates check (80% coverage)   │
│ └─ build-release-packages job:          │
│    ├─ Build solution                    │
│    ├─ Generate build number:            │
│    │  {branch}.{date}.{run}.{hash}      │
│    │  Example: main.20251024.1234.abc123│
│    ├─ Pack with CI version:             │
│    │  1.0.0-ci.main.20251024.1234.abc123│
│    └─ Store as artifacts (90 days)      │
└─────────────────────────────────────────┘
```

#### STAGE 2: create-release.yml workflow:

```
┌─────────────────────────────────────────┐
│ Job 1: validate-and-prepare             │
│ ├─ Check semantic version format        │
│ ├─ Verify tag doesn't already exist     │
│ ├─ Get commit SHA (input or latest main)│
│ └─ Find successful CI run for commit    │
└─────────────────────────────────────────┘
                 ↓
┌─────────────────────────────────────────┐
│ Job 2: download-packages                │
│ ├─ Download from CI workflow artifacts  │
│ ├─ Rename from CI version to release:   │
│ │  1.0.0-ci.main.20251024.1234.abc123  │
│ │       ↓                               │
│ │  1.0.0                                │
│ └─ Upload renamed packages               │
└─────────────────────────────────────────┘
                 ↓
┌─────────────────────────────────────────┐
│ Job 3: create-release                   │
│ ├─ Download renamed packages             │
│ ├─ Generate release notes               │
│ │  └─ Includes commits since last tag   │
│ │  └─ Links to CI run that built packages│
│ ├─ Create GitHub Release                │
│ │  └─ Tag: v{version}                   │
│ └─ Upload packages as release assets    │
└─────────────────────────────────────────┘
                 ↓
    ✅ Release Created!
                 ↓
┌─────────────────────────────────────────┐
│ IF NOT DRAFT: Auto-trigger              │
│ publish-nuget.yml workflow              │
└─────────────────────────────────────────┘
```

#### publish-nuget.yml workflow (auto-triggered):

```
┌─────────────────────────────────────────┐
│ Job 1: validate-release                 │
│ ├─ Extract version from tag             │
│ └─ Verify release assets exist          │
└─────────────────────────────────────────┘
                 ↓
┌─────────────────────────────────────────┐
│ Job 2: download-packages                │
│ ├─ Download .nupkg from release assets  │
│ ├─ Download .snupkg from release assets │
│ └─ Upload as workflow artifacts         │
└─────────────────────────────────────────┘
                 ↓
┌─────────────────────────────────────────┐
│ Job 3: publish-nuget                    │
│ Environment: production                 │
│ ├─ Authenticate via OIDC                │
│ └─ Push to NuGet.org                    │
└─────────────────────────────────────────┘
                 ↓
┌─────────────────────────────────────────┐
│ Job 4: publish-github-packages          │
│ └─ Push to GitHub Packages              │
└─────────────────────────────────────────┘
                 ↓
┌─────────────────────────────────────────┐
│ Job 5: update-release-notes             │
│ └─ Add publication status to release    │
└─────────────────────────────────────────┘
                 ↓
    ✅ Published to NuGet.org!
```

---

## 📝 Version Format

### Semantic Versioning (SemVer 2.0)

```
X.Y.Z[-prerelease]

X = Major version (breaking changes)
Y = Minor version (new features, backwards compatible)
Z = Patch version (bug fixes, backwards compatible)
-prerelease = Optional pre-release identifier
```

### Valid Examples

```bash
1.0.0           # Major release
1.1.0           # Minor update
1.1.1           # Patch
1.2.0-beta.1    # Beta pre-release
2.0.0-rc.1      # Release candidate
2.0.0-alpha     # Alpha pre-release
```

### Invalid Examples

```bash
1.0             # ❌ Missing patch version
v1.0.0          # ❌ Don't include 'v' prefix (added automatically)
1.0.0.0         # ❌ Too many segments
1.0.0-beta_1    # ❌ Use dot, not underscore
```

---

## 🎭 Release Types

### Production Release

```bash
# Settings:
version: 1.0.0
prerelease: false
draft: false

# Result:
- Marked as "Latest release" on GitHub
- Published immediately to NuGet.org
- Visible to all users
- Recommended for stable production releases
```

### Pre-release (Beta/RC/Alpha)

```bash
# Settings:
version: 1.1.0-beta.1
prerelease: true
draft: false

# Result:
- Marked as "Pre-release" on GitHub
- Published to NuGet.org with prerelease flag
- Users must opt-in to install
- Great for testing with early adopters
```

### Draft Release (Review Before Publishing)

```bash
# Settings:
version: 2.0.0
prerelease: false
draft: true

# Result:
- Not visible to public
- Not published to NuGet.org (yet)
- Can review and edit release notes
- Publish manually when ready
```

---

## 🔄 Draft Release Workflow

Use draft releases to review before going live:

### 1. Create Draft

```bash
gh workflow run create-release.yml \
  -f version=2.0.0 \
  -f draft=true
```

### 2. Review

1. Go to: https://github.com/KoalaFacts/HeroMessaging/releases
2. Find the draft release
3. Review:
   - Release notes (auto-generated from commits)
   - Attached packages (.nupkg files)
   - Version number

### 3. Edit (Optional)

1. Click **"Edit"** on the draft release
2. Update release notes
3. Add screenshots, migration guides, etc.
4. Save changes

### 4. Publish

1. Click **"Publish release"**
2. This triggers `publish-nuget.yml` automatically
3. Packages published to NuGet.org in ~5 minutes

---

## ⚠️ Common Scenarios

### Scenario 1: Tests Fail During Release

```
Problem: Unit tests fail in create-release workflow
```

**Solution**:
1. Fix the failing tests
2. Commit and push fixes to main
3. Re-run the create-release workflow with same version
4. If tag was created before failure, delete it first:
   ```bash
   git tag -d v1.0.0
   git push origin :refs/tags/v1.0.0
   ```

---

### Scenario 2: Wrong Version Number

```
Problem: Created release with wrong version (e.g., 1.0.0 instead of 1.1.0)
```

**Solution**:
1. Delete the GitHub release
2. Delete the tag:
   ```bash
   gh release delete v1.0.0 --yes
   git tag -d v1.0.0
   git push origin :refs/tags/v1.0.0
   ```
3. Re-run create-release workflow with correct version

---

### Scenario 3: Publish Fails, Need to Retry

```
Problem: NuGet publish failed due to network issue or rate limiting
```

**Solution**:
- The packages are already in the GitHub release assets
- Workflow can be retried from Actions tab
- No need to rebuild packages
- Or manually trigger publish (currently not supported, but can be added)

---

### Scenario 4: Emergency Hotfix

```
Problem: Critical bug found in production, need immediate fix
```

**Process**:
1. Create hotfix branch from main or release tag
2. Fix the bug
3. Run tests locally: `dotnet test`
4. Merge to main (or push to release branch)
5. Run create-release workflow:
   ```bash
   gh workflow run create-release.yml \
     -f version=1.0.1 \
     -f prerelease=false \
     -f draft=false
   ```
6. Monitor workflow, packages published in ~10-15 min

---

## 🔍 Monitoring Releases

### Check Workflow Status

```bash
# List recent workflow runs
gh run list --workflow=create-release.yml --limit 5

# Watch a specific run
gh run watch <run-id>

# View logs
gh run view <run-id> --log
```

### Verify Publication

After release is published, verify:

1. **NuGet.org**: https://www.nuget.org/packages/HeroMessaging/
   - Check version appears
   - Try installing: `dotnet add package HeroMessaging --version X.Y.Z`

2. **GitHub Packages**: https://github.com/KoalaFacts/HeroMessaging/packages
   - Verify package listed

3. **GitHub Release**: https://github.com/KoalaFacts/HeroMessaging/releases
   - Check release notes updated with publication status

---

## 🛡️ Security & Best Practices

### Version Planning

- **Major (X.0.0)**: Breaking changes, plan carefully
- **Minor (X.Y.0)**: New features, test thoroughly
- **Patch (X.Y.Z)**: Bug fixes, quick turnaround OK

### Pre-release Testing

Always use pre-releases for:
- Major version changes
- Significant new features
- Experimental functionality

### Environment Protection

The `production` environment requires:
- ✅ All tests must pass
- ✅ Packages built successfully
- ✅ NuGet trusted publisher configured

### Tag Management

- Tags are immutable - don't reuse
- If version was published to NuGet, can't republish same version
- Use pre-release versions for testing

---

## 📚 Troubleshooting

### Error: "Tag already exists"

```bash
# Check if tag exists
git tag -l | grep v1.0.0

# Delete local and remote tag
git tag -d v1.0.0
git push origin :refs/tags/v1.0.0
```

### Error: "No .nupkg files found in release assets"

**Cause**: create-release workflow didn't complete or upload failed

**Solution**:
1. Check create-release workflow logs
2. Re-run the workflow
3. Verify packages uploaded to release

### Error: "Version validation failed"

**Cause**: Invalid semantic version format

**Solution**: Use format `X.Y.Z` or `X.Y.Z-prerelease`
- ✅ `1.0.0`
- ✅ `1.0.0-beta.1`
- ❌ `v1.0.0` (remove 'v')
- ❌ `1.0` (add patch version)

---

## 🎯 Quick Reference

### Creating Releases

| Type | Version | Prerelease | Draft | Use Case |
|------|---------|------------|-------|----------|
| Production | `1.0.0` | `false` | `false` | Stable release |
| Beta | `1.1.0-beta.1` | `true` | `false` | Early testing |
| RC | `2.0.0-rc.1` | `true` | `false` | Release candidate |
| Draft | `1.0.0` | `false` | `true` | Review before publish |

### Workflow URLs

- **Create Release**: https://github.com/KoalaFacts/HeroMessaging/actions/workflows/create-release.yml
- **Publish to NuGet**: https://github.com/KoalaFacts/HeroMessaging/actions/workflows/publish-nuget.yml
- **Releases**: https://github.com/KoalaFacts/HeroMessaging/releases

### Commands

```bash
# Create production release
gh workflow run create-release.yml -f version=1.0.0

# Create pre-release
gh workflow run create-release.yml -f version=1.1.0-beta.1 -f prerelease=true

# Create draft
gh workflow run create-release.yml -f version=2.0.0 -f draft=true

# Check workflow status
gh run list --workflow=create-release.yml

# Delete release and tag
gh release delete v1.0.0 --yes
git push origin :refs/tags/v1.0.0
```

---

## 📞 Support

Questions? Issues?
- Check workflow logs in GitHub Actions
- See `.github/NUGET_TRUSTED_PUBLISHING.md` for NuGet setup
- See `.github/WORKFLOW_OVERVIEW.md` for complete workflow documentation
- Open an issue: https://github.com/KoalaFacts/HeroMessaging/issues

---

**Last Updated**: 2025-10-24
**Maintained By**: Development Team
