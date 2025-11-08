# Security Scan Review Guide

Complete guide for reviewing and responding to security scan results in HeroMessaging.

## Overview

HeroMessaging has **4 automated security scanning layers**:

1. **CodeQL Analysis** - Static code analysis for security vulnerabilities
2. **Secret Scanning** - Detects hardcoded credentials and API keys
3. **Dependency Vulnerabilities** - Scans NuGet packages for CVEs
4. **Dependabot Alerts** - Automated dependency update alerts

## Security Dashboard

### Accessing Security Scans

**Repository Security Tab:**
```
Repository → Security → Overview
```

**Quick Links:**
- Code scanning: `/security/code-scanning`
- Secret scanning: `/security/secret-scanning`
- Dependabot: `/security/dependabot`
- Security advisories: `/security/advisories`

## 1. CodeQL Analysis Review

### What CodeQL Scans For

**Security Issues:**
- SQL Injection
- Cross-Site Scripting (XSS)
- Path Traversal
- Command Injection
- Improper Input Validation
- Use of Unsafe APIs
- Cryptographic Issues

**Code Quality:**
- Dead code
- Unreachable code
- Type errors
- Logic errors

### Reviewing CodeQL Results

#### Access Results

```
Security → Code scanning alerts → CodeQL
```

Or check workflow:
```
Actions → CodeQL Security Analysis → Latest run
```

#### Understanding Alerts

Each alert shows:
- **Severity**: Critical, High, Medium, Low, Note
- **Rule**: Specific security rule violated
- **Location**: File and line number
- **Description**: What the issue is
- **Recommendation**: How to fix
- **Example**: Code example showing fix

#### Alert Example

```
Alert: SQL Injection vulnerability
Severity: High
Location: src/HeroMessaging/Data/MessageRepository.cs:45

Description:
User-provided data flows to a SQL query without sanitization.

Recommendation:
Use parameterized queries or an ORM to prevent SQL injection.

Code:
- string query = "SELECT * FROM Messages WHERE Id = " + userId;
+ string query = "SELECT * FROM Messages WHERE Id = @userId";
+ command.Parameters.AddWithValue("@userId", userId);
```

### Responding to CodeQL Alerts

#### Priority Matrix

| Severity | Action Timeline | Required Action |
|----------|----------------|-----------------|
| Critical | Fix immediately | Block PR, fix before merge |
| High | Fix within 7 days | Create issue, prioritize |
| Medium | Fix within 30 days | Add to backlog |
| Low | Fix when convenient | Document or suppress |
| Note | Review | May dismiss if false positive |

#### Fix Workflow

1. **Review Alert**
   ```bash
   # Click alert to see details
   # Read description and recommendation
   # Check code location
   ```

2. **Create Fix Branch**
   ```bash
   git checkout -b security/fix-codeql-alert-123
   ```

3. **Implement Fix**
   ```csharp
   // Follow CodeQL recommendation
   // Add tests to verify fix
   // Run CodeQL locally if possible
   ```

4. **Create PR**
   ```bash
   git commit -m "security: Fix SQL injection vulnerability (CodeQL-123)"
   git push origin security/fix-codeql-alert-123
   gh pr create --title "Security: Fix SQL injection (CodeQL-123)"
   ```

5. **Verify Fix**
   - PR triggers CodeQL scan
   - Check if alert disappears
   - Review comments may reference alert

6. **Close Alert**
   - Merge PR
   - Alert auto-closes when fixed code is in main
   - Or manually dismiss if false positive

#### Dismissing False Positives

If alert is a false positive:

```
1. Click alert
2. Click "Dismiss alert"
3. Select reason:
   - Won't fix
   - False positive
   - Used in tests
4. Add comment explaining why
5. Click "Dismiss alert"
```

Example dismissal comment:
```
This is a false positive. The SQL query is constructed from
a validated enum, not user input. The QueryBuilder class
ensures only whitelisted values are used.
```

## 2. Secret Scanning Review

### What Gitleaks Detects

**Credential Types:**
- API keys (AWS, Azure, Google, etc.)
- Database passwords
- Private keys
- OAuth tokens
- SSH keys
- Certificate files
- NuGet API keys

### Reviewing Secret Scan Results

#### Access Results

```
Actions → Security Scanning → Latest run → secret-scan job
```

Or download artifact:
```
Actions → Security Scanning → Artifacts → gitleaks-report
```

#### Understanding Gitleaks Output

```json
{
  "Description": "AWS Access Key",
  "StartLine": 15,
  "EndLine": 15,
  "StartColumn": 20,
  "EndColumn": 40,
  "Match": "AKIA****************",
  "Secret": "AKIAIOSFODNN7EXAMPLE",
  "File": "src/Config/AppSettings.cs",
  "Commit": "abc123def456",
  "Entropy": 5.2,
  "Author": "developer@example.com",
  "Date": "2025-11-07T12:00:00Z",
  "Message": "Add AWS configuration"
}
```

### Responding to Secret Leaks

#### 🚨 CRITICAL: Leaked Secrets

If secrets are found:

1. **Immediate Actions** (within 1 hour)
   ```bash
   # Rotate/revoke the compromised credential
   # AWS example:
   aws iam delete-access-key --access-key-id AKIA...

   # Generate new credential
   # Update in secure location (GitHub Secrets, Key Vault)
   ```

2. **Remove from Git History** (within 24 hours)
   ```bash
   # Use git-filter-repo to remove secret
   pip install git-filter-repo
   git filter-repo --path src/Config/AppSettings.cs --invert-paths

   # Force push (coordinate with team!)
   git push origin --force --all
   ```

3. **Prevent Future Leaks**
   ```bash
   # Add pattern to .gitignore
   echo "*.key" >> .gitignore
   echo "secrets.*" >> .gitignore

   # Use environment variables
   export API_KEY="..."

   # Use GitHub Secrets in workflows
   ${{ secrets.API_KEY }}
   ```

4. **Notify Team**
   - Create security incident
   - Document lessons learned
   - Update security training

#### False Positives

Common false positives:
- Test data / mock credentials
- Example code / documentation
- High-entropy strings (not actually secrets)

To exclude false positives, add to `.gitleaksignore`:
```
# Test credentials
src/Tests/TestData/mock-credentials.json
# Documentation examples
docs/examples/api-example.cs:15
```

## 3. Dependency Vulnerability Review

### What Dependency Scan Detects

**Vulnerabilities:**
- Known CVEs in NuGet packages
- Security advisories
- Outdated packages with fixes
- Transitive dependency issues

### Reviewing Vulnerability Results

#### Access Results

```
Actions → Security Scanning → Latest run → dependency-scan job
```

Or check Dependabot:
```
Security → Dependabot alerts
```

#### Understanding Vulnerability Reports

```
The following sources were used for vulnerability data:

https://api.nuget.org/v3/index.json

The following vulnerable packages were found:
   [net8.0]:
   Top-level Package        Requested   Resolved   Severity   Advisory URL
   > Newtonsoft.Json        12.0.1      12.0.1     High       https://github.com/advisories/GHSA-5crp-9r3c-p9vr

   Transitive Package       Resolved   Severity   Advisory URL
   > System.Text.Json       6.0.0      6.0.0      High       https://github.com/advisories/GHSA-hh2w-p6rv-4g7w
```

### Responding to Vulnerabilities

#### Priority Matrix

| Severity | CVSS Score | Action Timeline | Required Action |
|----------|-----------|----------------|-----------------|
| Critical | 9.0-10.0  | Fix immediately | Block releases |
| High | 7.0-8.9 | Fix within 7 days | Create hotfix |
| Medium | 4.0-6.9 | Fix within 30 days | Regular update |
| Low | 0.1-3.9 | Fix when convenient | Plan update |

#### Fix Workflow

1. **Assess Impact**
   ```bash
   # Check if vulnerable code path is used
   # Review advisory details
   # Determine if exploit is practical
   ```

2. **Update Package**
   ```bash
   # Update to patched version
   dotnet add package Newtonsoft.Json --version 13.0.3

   # Or update all packages
   dotnet list package --outdated
   dotnet add package [package-name]
   ```

3. **Test Changes**
   ```bash
   # Run all tests
   dotnet test

   # Check for breaking changes
   # Verify functionality still works
   ```

4. **Document and Deploy**
   ```bash
   git commit -m "security: Update Newtonsoft.Json to 13.0.3 (fixes CVE-2024-1234)"
   git push
   ```

#### When Update Isn't Available

If no patch exists:

1. **Mitigate Risk**
   - Disable vulnerable feature
   - Add input validation
   - Implement workarounds from advisory

2. **Monitor for Updates**
   - Subscribe to security advisories
   - Check weekly for patches
   - Consider alternative packages

3. **Document Decision**
   ```markdown
   ## Security Exception: CVE-2024-1234

   **Package**: ExampleLib 1.2.3
   **Severity**: High
   **Status**: No patch available

   **Mitigation**:
   - Disabled JSON deserialization feature
   - Added input sanitization
   - Limited exposure to internal APIs only

   **Review Date**: 2025-12-01
   ```

## 4. Dependabot Alert Review

### What Dependabot Monitors

**Alerts For:**
- Security vulnerabilities in dependencies
- Outdated packages
- Deprecated packages

**Auto-Creates:**
- Pull requests with updates
- Security advisories
- Version comparison info

### Reviewing Dependabot PRs

#### Good PR Example
```
chore(deps): Bump Microsoft.Extensions.Logging from 8.0.0 to 8.0.1

Bumps Microsoft.Extensions.Logging from 8.0.0 to 8.0.1.
Release notes: https://github.com/dotnet/runtime/releases/tag/v8.0.1

Fixes:
- CVE-2024-1234 (High): Buffer overflow in log formatting

Dependabot compatibility score: 100%
```

#### Review Checklist

Before merging Dependabot PR:

- [ ] **Check CI Status**: All tests pass
- [ ] **Review Changes**: Changelog and release notes
- [ ] **Verify Compatibility**: No breaking changes
- [ ] **Security Fix**: If CVE, verify it's fixed
- [ ] **Test Locally**: For major updates
- [ ] **Update Timeline**: Security fixes merge ASAP

#### Auto-Merge Strategy

For low-risk updates:

```yaml
# .github/dependabot.yml
version: 2
updates:
  - package-ecosystem: "nuget"
    open-pull-requests-limit: 10
    # Enable auto-merge for patch updates
    groups:
      patch-updates:
        update-types: ["patch"]
```

Then enable auto-merge:
```bash
gh pr merge --auto --squash [PR-number]
```

## Security Scan Dashboard

### Weekly Review Checklist

**Monday Morning Review** (15-30 minutes):

- [ ] Check CodeQL alerts: `security/code-scanning`
- [ ] Review secret scan results: Latest workflow run
- [ ] Check dependency vulnerabilities: `security/dependabot`
- [ ] Review Dependabot PRs: Merge safe updates
- [ ] Check security advisories: Any new CVEs?
- [ ] Update tracking spreadsheet/dashboard

### Metrics to Track

**Security Health Metrics:**
```
┌─────────────────────────────────────────┐
│ Security Scan Metrics (Week of Nov 7)  │
├─────────────────────────────────────────┤
│ CodeQL Alerts                           │
│   Critical: 0  High: 2  Medium: 5       │
│   Trend: ↓ -3 from last week            │
├─────────────────────────────────────────┤
│ Secret Scanning                         │
│   Secrets Found: 0                      │
│   False Positives: 2                    │
├─────────────────────────────────────────┤
│ Dependency Vulnerabilities              │
│   Critical: 0  High: 1  Medium: 3       │
│   Average Age: 12 days                  │
├─────────────────────────────────────────┤
│ Dependabot PRs                          │
│   Open: 5  Merged This Week: 8          │
│   Auto-merged: 6                        │
└─────────────────────────────────────────┘
```

## Incident Response

### Security Incident Severity

**P0 - Critical** (Respond within 1 hour)
- Active exploitation
- Credentials leaked
- Critical vulnerability in production

**P1 - High** (Respond within 24 hours)
- High-severity CVE
- Multiple medium vulnerabilities
- Security scan failures blocking release

**P2 - Medium** (Respond within 7 days)
- Medium-severity vulnerabilities
- Security best practice violations
- Non-critical security improvements

**P3 - Low** (Respond within 30 days)
- Low-severity issues
- Security documentation updates
- Proactive improvements

### Incident Response Playbook

1. **Assess** (5-15 minutes)
   - Determine severity
   - Identify affected systems
   - Check for active exploitation

2. **Contain** (15-60 minutes)
   - Rotate compromised credentials
   - Disable vulnerable features
   - Block malicious traffic

3. **Remediate** (1-24 hours)
   - Apply security patches
   - Update vulnerable dependencies
   - Deploy fixes to production

4. **Recover** (1-7 days)
   - Verify fixes work
   - Monitor for issues
   - Update documentation

5. **Learn** (1-2 weeks)
   - Post-incident review
   - Update procedures
   - Train team on lessons learned

## Tools and Resources

### Useful Commands

```bash
# Check for vulnerable packages
dotnet list package --vulnerable

# Update all packages to latest
dotnet list package --outdated | grep ">" | awk '{print $2}' | xargs -I {} dotnet add package {}

# Run local security scan (requires GitHub CLI)
gh api repos/KoalaFacts/HeroMessaging/code-scanning/alerts

# Check Dependabot alerts
gh api repos/KoalaFacts/HeroMessaging/dependabot/alerts

# Trigger security scan manually
gh workflow run security-scan.yml
```

### External Resources

- [NVD - National Vulnerability Database](https://nvd.nist.gov/)
- [GitHub Security Advisories](https://github.com/advisories)
- [OWASP Top 10](https://owasp.org/www-project-top-ten/)
- [CWE - Common Weakness Enumeration](https://cwe.mitre.org/)

## Summary

✅ **4 Security Layers**: CodeQL, Secrets, Dependencies, Dependabot
📊 **Weekly Reviews**: Check all scans every Monday
🚨 **Incident Response**: P0 within 1 hour, P1 within 24 hours
📝 **Documentation**: Track metrics and incidents
🔧 **Automation**: Most scans run automatically

Stay vigilant and security-conscious! 🔒

---

**Last Updated**: 2025-11-07
**Version**: 1.0
**Review Frequency**: Weekly (Mondays)
