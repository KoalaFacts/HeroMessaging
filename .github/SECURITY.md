# Security Policy

## Supported Versions

We release security updates for the following versions:

| Version | Supported          |
| ------- | ------------------ |
| 1.x.x   | :white_check_mark: |
| < 1.0   | :x:                |

## Reporting a Vulnerability

We take security seriously. If you discover a security vulnerability, please follow these steps:

### 1. Do Not Disclose Publicly

Please do **not** create a public GitHub issue for security vulnerabilities.

### 2. Report Privately

- Use GitHub's [Security Advisories](https://github.com/KoalaFacts/HeroMessaging/security/advisories/new) feature
- Or email the maintainers directly (if email is provided in CLAUDE.md or repository settings)

### 3. Include Details

When reporting, please include:

- Description of the vulnerability
- Steps to reproduce
- Potential impact
- Suggested fix (if you have one)
- Your contact information (optional)

### 4. Response Timeline

- **Initial Response**: Within 48 hours
- **Status Update**: Within 7 days
- **Fix Timeline**: Depends on severity
  - Critical: 7-14 days
  - High: 14-30 days
  - Medium: 30-60 days
  - Low: Next regular release

## Security Features

### Automated Security Scanning

This repository implements multiple layers of automated security:

#### 1. **Dependabot** (Dependency Scanning)
- **What**: Automated dependency updates and vulnerability alerts
- **When**: Weekly scans on Mondays at 6:00 AM UTC
- **Coverage**: NuGet packages and GitHub Actions
- **Location**: `.github/dependabot.yml`

#### 2. **Secret Scanning** (Gitleaks)
- **What**: Detects hardcoded secrets, API keys, and credentials
- **When**: Every push, PR, and daily at 4:00 AM UTC
- **Tool**: [Gitleaks](https://github.com/gitleaks/gitleaks)
- **Location**: `.github/workflows/security-scan.yml`

#### 3. **Vulnerability Scanning**
- **What**: Scans NuGet packages for known vulnerabilities
- **When**: Every push, PR, and daily scans
- **Tool**: `dotnet list package --vulnerable`
- **Action**: Fails CI if critical vulnerabilities found

#### 4. **SBOM Generation**
- **What**: Software Bill of Materials (SPDX format)
- **When**: Generated during package build
- **Tool**: Microsoft SBOM Tool
- **Purpose**: Supply chain transparency and security

#### 5. **Package Signing** (Optional)
- **What**: Code signing for NuGet packages
- **When**: During release creation
- **Status**: Configured but requires certificate
- **Setup Instructions**: See [Package Signing Setup](#package-signing-setup)

### Security Best Practices

This project follows these security practices:

✅ **Minimal Permissions**: GitHub Actions use principle of least privilege
✅ **Trusted Publishing**: NuGet.org publishing via OIDC (no API keys)
✅ **Immutable Builds**: Packages built once, never rebuilt for releases
✅ **Artifact Retention**: 90-day retention for audit trails
✅ **Secret Protection**: No secrets in code or git history
✅ **Dependency Pinning**: Action versions pinned (e.g., `@v5`)
✅ **HTTPS Only**: All external connections use HTTPS
✅ **Code Review**: All changes require review before merge

## Package Signing Setup

To enable NuGet package signing:

### 1. Obtain a Code Signing Certificate

Purchase or generate a code signing certificate from a trusted Certificate Authority:
- [DigiCert](https://www.digicert.com/signing/code-signing-certificates)
- [Sectigo](https://sectigo.com/ssl-certificates-tls/code-signing)
- [GlobalSign](https://www.globalsign.com/en/code-signing-certificate)

### 2. Export Certificate

Export your certificate as a `.pfx` file with password protection:

```bash
# If you have a .p12 or .pfx file, convert to base64
base64 -w 0 your-certificate.pfx > certificate.base64
```

### 3. Add Secrets to Repository

Add these secrets to your GitHub repository:

1. Go to **Settings** → **Secrets and variables** → **Actions**
2. Add the following secrets:
   - `NUGET_SIGNING_CERT_BASE64`: Content of `certificate.base64`
   - `NUGET_SIGNING_CERT_PASSWORD`: Password for the .pfx file

### 4. Verify

On the next release, the `create-release` workflow will automatically sign packages.

## Security Workflow Diagram

```
┌─────────────────────────────────────────────────────────┐
│                    Code Pushed                          │
└────────────────────┬────────────────────────────────────┘
                     │
     ┌───────────────┼───────────────┐
     │               │               │
     ▼               ▼               ▼
┌─────────┐   ┌──────────┐   ┌──────────────┐
│ Secret  │   │Dependency│   │  Unit Tests  │
│ Scan    │   │  Vuln    │   │  + Coverage  │
│(Gitleaks)│  │  Scan    │   │              │
└─────────┘   └──────────┘   └──────────────┘
     │               │               │
     └───────────────┼───────────────┘
                     │
                     ▼
            ┌─────────────────┐
            │  Quality Gates  │
            │  (80% coverage) │
            └────────┬────────┘
                     │
                     ▼
            ┌─────────────────┐
            │  Build Package  │
            │  + Generate SBOM│
            └────────┬────────┘
                     │
                     ▼
            ┌─────────────────┐
            │  Sign Package   │
            │  (if cert set)  │
            └────────┬────────┘
                     │
                     ▼
            ┌─────────────────┐
            │  Publish to     │
            │  NuGet.org      │
            └─────────────────┘
```

## Verifying Package Authenticity

### Verify Signature

```bash
dotnet nuget verify HeroMessaging.1.0.0.nupkg
```

### Check SBOM

SBOM files are included in release artifacts:
1. Go to [Releases](https://github.com/KoalaFacts/HeroMessaging/releases)
2. Download SBOM artifacts for the version
3. Review `_manifest/spdx_2.2/*.spdx.json`

### Validate Package Hash

```bash
# On Linux/macOS
sha256sum HeroMessaging.1.0.0.nupkg

# On Windows (PowerShell)
Get-FileHash HeroMessaging.1.0.0.nupkg -Algorithm SHA256
```

Compare the hash with the one listed in the release notes.

## Security Updates

Security updates are released as soon as possible after a vulnerability is confirmed:

1. **Patch Created**: Fix developed and tested
2. **Security Advisory**: Published on GitHub
3. **Release**: New version released with fix
4. **Notification**: Dependabot alerts users with updated version
5. **Documentation**: CHANGELOG.md updated with security note

## Acknowledgments

We appreciate security researchers who responsibly disclose vulnerabilities. Contributors will be acknowledged in:

- GitHub Security Advisories
- Release notes
- CHANGELOG.md (with permission)

## Additional Resources

- [OWASP Top 10](https://owasp.org/www-project-top-ten/)
- [NuGet Package Signing](https://docs.microsoft.com/nuget/create-packages/sign-a-package)
- [GitHub Security Features](https://github.com/features/security)
- [Supply Chain Security](https://slsa.dev/)

---

**Last Updated**: 2025-11-07
**Version**: 1.0
