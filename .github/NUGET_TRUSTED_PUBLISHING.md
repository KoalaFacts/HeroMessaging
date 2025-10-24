# NuGet Trusted Publishing Setup Guide

This repository uses **NuGet Trusted Publishing** for secure, keyless package publishing to NuGet.org.

## What is Trusted Publishing?

Trusted Publishing allows GitHub Actions to publish packages to NuGet.org without requiring API keys. Instead, it uses OpenID Connect (OIDC) tokens to verify the authenticity of the publishing workflow.

### Benefits:
- ✅ **No API keys to manage** - No secrets to rotate or leak
- ✅ **Enhanced security** - Short-lived tokens scoped to specific workflows
- ✅ **Better audit trail** - NuGet.org knows exactly which workflow published each package
- ✅ **Automatic setup** - Once configured, works automatically for all future releases

## Prerequisites

1. **NuGet.org account** with ownership rights to the package namespace
2. **GitHub repository** at `KoalaFacts/HeroMessaging`
3. **Package reservation** on NuGet.org (recommended)

---

## Setup Instructions

### Step 1: Reserve Package ID on NuGet.org (Recommended)

Before configuring trusted publishing, it's best to reserve your package IDs:

1. Go to https://www.nuget.org/packages/manage/upload
2. Upload a minimal version (0.0.1-alpha) of:
   - `HeroMessaging`
   - `HeroMessaging.Abstractions`
3. This establishes your ownership and allows trusted publisher configuration

**Alternative**: You can configure trusted publishing first, but the package must not exist yet.

---

### Step 2: Configure Trusted Publishers on NuGet.org

#### For HeroMessaging Package:

1. Go to https://www.nuget.org/packages/HeroMessaging/manage
2. Scroll to **"Trusted publishers"** section
3. Click **"Add trusted publisher"**
4. Fill in the form:
   - **Owner**: `KoalaFacts`
   - **Repository**: `HeroMessaging`
   - **Workflow**: `publish-nuget.yml`
   - **Environment**: `production`
   - **Subject identifier** (optional): Leave empty or use `repo:KoalaFacts/HeroMessaging:environment:production`
5. Click **"Add"**

#### For HeroMessaging.Abstractions Package:

Repeat the same process for:
- https://www.nuget.org/packages/HeroMessaging.Abstractions/manage

**Configuration values:**
```yaml
Owner: KoalaFacts
Repository: HeroMessaging
Workflow: publish-nuget.yml
Environment: production
```

---

### Step 3: Configure GitHub Environment (Already Done)

The workflow is already configured with the `production` environment. No action needed.

**Already configured in `publish-nuget.yml`:**
```yaml
environment:
  name: production
  url: https://www.nuget.org/packages/HeroMessaging
```

---

### Step 4: Test the Configuration

#### Option A: Create a Test Release

1. Create a Git tag:
   ```bash
   git tag v0.1.0-alpha
   git push origin v0.1.0-alpha
   ```

2. Create a GitHub Release:
   - Go to https://github.com/KoalaFacts/HeroMessaging/releases/new
   - Select the tag `v0.1.0-alpha`
   - Mark as **pre-release**
   - Click **Publish release**

3. The workflow will automatically trigger and publish to NuGet.org

#### Option B: Manual Workflow Dispatch

1. Go to https://github.com/KoalaFacts/HeroMessaging/actions/workflows/publish-nuget.yml
2. Click **"Run workflow"**
3. Enter:
   - **Version**: `0.1.0-alpha`
   - **Is prerelease**: `true`
4. Click **"Run workflow"**

---

### Step 5: Verify Publication

1. Check the workflow run succeeded:
   - https://github.com/KoalaFacts/HeroMessaging/actions/workflows/publish-nuget.yml

2. Verify packages on NuGet.org:
   - https://www.nuget.org/packages/HeroMessaging
   - https://www.nuget.org/packages/HeroMessaging.Abstractions

3. Check GitHub Packages:
   - https://github.com/KoalaFacts/HeroMessaging/packages

---

## Workflow Behavior

### Automatic Publishing (Recommended)

When you create a GitHub Release:
- Workflow detects the release event
- Extracts version from release tag (e.g., `v1.2.3` → `1.2.3`)
- Runs tests to validate the build
- Builds and packs NuGet packages
- Publishes to NuGet.org using trusted publishing
- Publishes to GitHub Packages
- Updates release notes with package links

### Manual Publishing

You can manually trigger the workflow for ad-hoc releases:
```bash
# Via GitHub UI: Actions > Publish to NuGet > Run workflow
# Or via gh CLI:
gh workflow run publish-nuget.yml -f version=1.0.0 -f prerelease=false
```

---

## Troubleshooting

### Error: "Trusted publisher not configured"

**Cause**: NuGet.org doesn't have the trusted publisher configuration for your package.

**Solution**:
1. Verify you have ownership of the package on NuGet.org
2. Follow **Step 2** above to add the trusted publisher configuration
3. Ensure the values match exactly:
   - Owner: `KoalaFacts`
   - Repository: `HeroMessaging`
   - Workflow: `publish-nuget.yml`
   - Environment: `production`

### Error: "Package already exists"

**Cause**: You're trying to publish a version that already exists.

**Solution**:
- Increment the version number
- Or delete the existing package version on NuGet.org (if it's a mistake)

### Error: "The environment 'production' was not found"

**Cause**: GitHub environment doesn't exist in repository settings.

**Solution**:
1. Go to https://github.com/KoalaFacts/HeroMessaging/settings/environments
2. Click **"New environment"**
3. Name it: `production`
4. Click **"Configure environment"**
5. (Optional) Add protection rules:
   - ✅ Required reviewers (for production releases)
   - ✅ Limit to protected branches only

### Error: "Tests failed"

**Cause**: Unit tests must pass before publishing.

**Solution**:
- Fix the failing tests
- Ensure all tests pass locally: `dotnet test --filter Category=Unit`
- Push the fixes and retry

---

## Security Considerations

### What tokens does the workflow have access to?

The workflow uses:
1. **GITHUB_TOKEN** (built-in, scoped to the repository)
   - Used for: Creating comments, uploading artifacts, GitHub Packages
   - Permissions: Explicitly limited in workflow (contents: read, packages: write)

2. **OIDC Token** (short-lived, workflow-scoped)
   - Used for: NuGet.org authentication
   - Permissions: Only valid for the specific workflow and environment
   - Lifetime: ~15 minutes

### Can someone fork and publish malicious packages?

**No.** Trusted publishing is configured per-repository:
- Only workflows from `KoalaFacts/HeroMessaging` can publish
- Only the `publish-nuget.yml` workflow is authorized
- Only when running in the `production` environment
- Forks cannot access the environment or OIDC tokens

### Protection Rules (Recommended)

Add environment protection rules at:
https://github.com/KoalaFacts/HeroMessaging/settings/environments/production

**Recommended settings:**
1. ✅ **Required reviewers** - Require manual approval before publishing
2. ✅ **Limit to protected branches** - Only allow `main` or `release/*` branches
3. ⚠️ **Wait timer** - Optional: Add a 5-minute wait to review deployment

---

## Migration from API Key (If Needed)

If you previously used API keys and need to temporarily switch back:

1. Uncomment the API key section in `publish-nuget.yml` (lines 176-184)
2. Comment out the trusted publishing section (lines 166-174)
3. Add `NUGET_API_KEY` to GitHub Secrets
4. Update the workflow

**Not recommended** - Trusted publishing is more secure and requires no maintenance.

---

## Additional Resources

- [NuGet Trusted Publishers Documentation](https://learn.microsoft.com/en-us/nuget/nuget-org/publish-a-package#trusted-publishers)
- [GitHub OIDC with NuGet](https://docs.github.com/en/actions/deployment/security-hardening-your-deployments/configuring-openid-connect-in-nuget)
- [NuGet.org Package Management](https://www.nuget.org/account/Packages)
- [GitHub Environments](https://docs.github.com/en/actions/deployment/targeting-different-environments/using-environments-for-deployment)

---

## Support

If you encounter issues:
1. Check the workflow logs in GitHub Actions
2. Verify NuGet.org trusted publisher configuration
3. Review this guide's troubleshooting section
4. Open an issue in the repository

**Package owners**: Contact through NuGet.org package management page
