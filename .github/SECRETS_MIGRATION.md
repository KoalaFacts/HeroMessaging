# GitHub Secrets Migration Guide

This document describes the GitHub Secrets that need to be configured for the HeroMessaging workflows.

## Required Secrets for Integration Tests

The following secrets should be added to your GitHub repository settings at:
`Settings > Secrets and variables > Actions > New repository secret`

### Database Test Credentials

#### 1. TEST_POSTGRES_PASSWORD
- **Description**: Password for PostgreSQL test database
- **Recommended Value**: Generate a strong random password (e.g., use `openssl rand -base64 32`)
- **Usage**: Integration tests for PostgreSQL storage plugin
- **Workflow**: `test-matrix.yml`

#### 2. TEST_SQLSERVER_PASSWORD
- **Description**: Password for SQL Server test database
- **Requirements**: Must meet SQL Server password complexity requirements:
  - At least 8 characters
  - Contains uppercase, lowercase, numbers, and special characters
- **Recommended Value**: Generate a strong random password (e.g., `HeroMessaging_$(openssl rand -base64 16)!`)
- **Usage**: Integration tests for SQL Server storage plugin
- **Workflow**: `test-matrix.yml`

## Current Status

⚠️ **SECURITY WARNING**: Currently, test database passwords are hardcoded in `test-matrix.yml`:
- Line 208: `POSTGRES_PASSWORD: postgres`
- Line 214: `SA_PASSWORD: HeroMessaging123!`

## Migration Steps

### Step 1: Add Secrets to GitHub

1. Navigate to your repository on GitHub
2. Go to **Settings** > **Secrets and variables** > **Actions**
3. Click **New repository secret**
4. Add each secret:
   - Name: `TEST_POSTGRES_PASSWORD`
   - Value: `<your-secure-password>`

   - Name: `TEST_SQLSERVER_PASSWORD`
   - Value: `<your-secure-password>`

### Step 2: Update test-matrix.yml

Replace the hardcoded passwords in `.github/workflows/test-matrix.yml`:

**Before (lines 207-215):**
```yaml
postgres:
  image: postgres:15
  env:
    POSTGRES_PASSWORD: postgres
    POSTGRES_DB: heromessaging_test

sqlserver:
  image: mcr.microsoft.com/mssql/server:2022-latest
  env:
    SA_PASSWORD: HeroMessaging123!
    ACCEPT_EULA: Y
```

**After:**
```yaml
postgres:
  image: postgres:15
  env:
    POSTGRES_PASSWORD: ${{ secrets.TEST_POSTGRES_PASSWORD }}
    POSTGRES_DB: heromessaging_test

sqlserver:
  image: mcr.microsoft.com/mssql/server:2022-latest
  env:
    SA_PASSWORD: ${{ secrets.TEST_SQLSERVER_PASSWORD }}
    ACCEPT_EULA: Y
```

### Step 3: Update Connection Strings

Update the connection strings in the integration tests job (lines 268-271):

**Before:**
```yaml
env:
  PostgreSQL__ConnectionString: "Host=localhost;Port=5432;Database=heromessaging_test;Username=postgres;Password=postgres"
  SqlServer__ConnectionString: "Server=localhost,1433;Database=heromessaging_test;User Id=sa;Password=HeroMessaging123!;TrustServerCertificate=true"
  Redis__ConnectionString: "localhost:6379"
```

**After:**
```yaml
env:
  PostgreSQL__ConnectionString: "Host=localhost;Port=5432;Database=heromessaging_test;Username=postgres;Password=${{ secrets.TEST_POSTGRES_PASSWORD }}"
  SqlServer__ConnectionString: "Server=localhost,1433;Database=heromessaging_test;User Id=sa;Password=${{ secrets.TEST_SQLSERVER_PASSWORD }};TrustServerCertificate=true"
  Redis__ConnectionString: "localhost:6379"
```

### Step 4: Verify

1. Commit and push the changes
2. Trigger a workflow run (push to a branch or manually trigger)
3. Verify that integration tests pass with the new secrets

## Optional Secrets for Future Features

### NUGET_API_KEY (for NuGet publishing)
- **Description**: API key for publishing to NuGet.org
- **Recommendation**: Use **NuGet Trusted Publishing** instead (no API key needed)
- **Alternative**: If not using trusted publishing, generate API key at https://www.nuget.org/account/apikeys

### CODECOV_TOKEN (for Codecov)
- **Description**: Token for uploading coverage to Codecov
- **Current Status**: Not required (using Codecov's GitHub integration)
- **When Needed**: If you need more granular control or private repositories

## Security Best Practices

1. **Never commit secrets to the repository**
2. **Rotate secrets regularly** (every 90 days recommended)
3. **Use minimal permissions** for service accounts
4. **Audit secret access** through GitHub's audit log
5. **Use environment-specific secrets** when deploying to multiple environments

## Additional Resources

- [GitHub Encrypted Secrets Documentation](https://docs.github.com/en/actions/security-guides/encrypted-secrets)
- [NuGet Trusted Publishing](https://devblogs.microsoft.com/nuget/introducing-package-source-mapping/)
- [SQL Server Password Policy](https://docs.microsoft.com/en-us/sql/relational-databases/security/password-policy)
