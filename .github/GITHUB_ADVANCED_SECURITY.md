# GitHub Advanced Security Setup Guide

Complete guide for enabling and configuring GitHub Advanced Security (GHAS) features for HeroMessaging.

## Overview

GitHub Advanced Security provides enterprise-grade security features:

- 🔍 **CodeQL Analysis** - Advanced semantic code scanning
- 🔐 **Secret Scanning** - Native GitHub secret detection
- 🛡️ **Dependency Review** - Vulnerability detection in PRs
- 📊 **Security Overview** - Centralized security dashboard
- 🔔 **Advanced Notifications** - Customizable security alerts

## Availability

### Public Repositories
✅ **FREE** - All GHAS features available for public repos

### Private Repositories
💰 **Paid** - Requires GitHub Enterprise or GitHub Advanced Security license

**Pricing** (as of 2025):
- GitHub Enterprise Cloud: $21/user/month (includes GHAS)
- GHAS Add-on: $49/committer/month
- GitHub Enterprise Server: $21/user/month (includes GHAS)

**Check Repository Type:**
```bash
gh api repos/KoalaFacts/HeroMessaging --jq '.private'
# false = public (free GHAS)
# true = private (requires license)
```

## Quick Setup

### For Public Repositories

1. **Enable Code Scanning**
   ```
   Repository → Settings → Code security and analysis
   → Code scanning → Set up → Advanced
   ```

2. **Enable Secret Scanning**
   ```
   Repository → Settings → Code security and analysis
   → Secret scanning → Enable
   ```

3. **Enable Push Protection**
   ```
   Repository → Settings → Code security and analysis
   → Push protection → Enable
   ```

4. **Configure Dependabot**
   ```
   Repository → Settings → Code security and analysis
   → Dependabot alerts → Enable
   → Dependabot security updates → Enable
   ```

### For Private Repositories

**Prerequisites:**
- GitHub Enterprise account OR
- GHAS license purchased

**Then follow same steps as public repos above**

## Feature Setup Guide

### 1. CodeQL Code Scanning

#### What It Does
- Analyzes code for security vulnerabilities
- Detects common coding errors
- Finds code quality issues
- Supports 10+ languages

#### Setup Steps

**Option A: Use Existing Workflow** (Recommended)

Our repository includes `.github/workflows/codeql.yml`:
```yaml
name: CodeQL Security Analysis
on:
  push:
    branches: [ main, develop ]
  pull_request:
    branches: [ main, develop ]
  schedule:
    - cron: '0 5 * * 1'  # Weekly
```

Just enable in settings:
```
Settings → Code security → Code scanning → Enable
```

**Option B: GitHub Default Setup**

```
Settings → Code security → Code scanning
→ Set up → Default
→ Select languages: C#
→ Query suite: Extended
→ Enable
```

#### Configuration Options

**Query Suites:**
- **Default**: Standard security queries (recommended for start)
- **Extended**: Additional security queries (our configuration)
- **Security and Quality**: Most comprehensive (may have more false positives)

**Custom Configuration:**

Create `.github/codeql/codeql-config.yml`:
```yaml
name: "HeroMessaging CodeQL Config"
queries:
  - uses: security-extended
  - uses: security-and-quality

paths-ignore:
  - '**/bin/**'
  - '**/obj/**'
  - '**/Tests/**/*.cs'

query-filters:
  - exclude:
      id: cs/weak-crypto  # If using approved weak crypto intentionally
```

#### Viewing Results

```
Security → Code scanning alerts
```

Each alert shows:
- Severity (Critical, High, Medium, Low)
- CWE classification
- Affected code location
- Fix recommendations
- Related pull requests

### 2. Secret Scanning

#### What It Does
- Detects ~200 token types
- Scans commits, PRs, and issues
- Notifies security contacts
- Optionally blocks pushes

#### Setup Steps

1. **Enable Secret Scanning**
   ```
   Settings → Code security → Secret scanning
   → Enable secret scanning
   ```

2. **Enable Push Protection** (Highly Recommended)
   ```
   Settings → Code security → Push protection
   → Enable push protection
   ```

3. **Configure Notifications**
   ```
   Settings → Notifications
   → Security alerts → Enable
   ```

#### Supported Secret Types

**Cloud Providers:**
- AWS (access keys, secret keys)
- Azure (connection strings, SAS tokens)
- Google Cloud (API keys, service accounts)

**Services:**
- GitHub (personal access tokens)
- NuGet (API keys)
- Slack (webhooks, tokens)
- Stripe (API keys)
- Twilio (auth tokens)

**Full List:** https://docs.github.com/en/code-security/secret-scanning/secret-scanning-patterns

#### Push Protection Example

When developer tries to commit a secret:
```bash
git push origin feature-branch

remote: error: GH013: Secret scanning detected the following secrets:
remote:
remote: - AWS Access Key ID
remote:   Location: src/Config.cs:15
remote:   Value: AKIA****************
remote:
remote: Push blocked. Remove the secret or bypass with:
remote:   git push --push-option=secret-bypass
```

#### Bypassing Push Protection

**For False Positives Only:**
```bash
# Add bypass comment
git push --push-option="secret-bypass=This is test data, not a real secret"
```

**Better Approach:**
```bash
# Remove the secret
git reset HEAD~1
# Move secret to environment variable or GitHub Secrets
# Commit without secret
git commit -m "Use environment variable for API key"
git push
```

### 3. Dependency Review

#### What It Does
- Reviews dependencies in pull requests
- Shows newly added vulnerabilities
- Displays license changes
- Prevents merging vulnerable dependencies

#### Setup Steps

1. **Enable Dependency Graph**
   ```
   Settings → Code security → Dependency graph
   → Enable dependency graph
   ```

2. **Enable Dependabot Alerts**
   ```
   Settings → Code security → Dependabot alerts
   → Enable Dependabot alerts
   ```

3. **Enable Dependency Review**
   ```
   (Automatically enabled with Dependabot alerts)
   ```

#### How It Works

**On Every Pull Request:**
1. Scans package changes
2. Checks for vulnerabilities
3. Shows security impact
4. Comments on PR with findings

**PR Comment Example:**
```markdown
## Dependency Review

⚠️ **2 new vulnerabilities** detected

| Package | Version | Severity | Advisory |
|---------|---------|----------|----------|
| Newtonsoft.Json | 12.0.1 | High | GHSA-5crp-9r3c-p9vr |
| System.Text.Json | 6.0.0 | Critical | GHSA-hh2w-p6rv-4g7w |

**Recommendation**: Update to patched versions before merging.
```

#### Branch Protection Integration

Require dependency review to pass:
```
Settings → Branches → Branch protection rules → Add rule
→ Branch name pattern: main
→ ☑️ Require status checks to pass
→ ☑️ dependency-review
```

### 4. Security Overview Dashboard

#### What It Shows
- All security alerts in one place
- Trends over time
- Team security metrics
- Compliance status

#### Access Dashboard

**Repository Level:**
```
Security → Overview
```

**Organization Level** (if applicable):
```
Organization → Security → Overview
```

#### Key Metrics

**Security Posture:**
```
┌───────────────────────────────────┐
│ Security Overview                 │
├───────────────────────────────────┤
│ Code Scanning                     │
│   Critical: 0  High: 2            │
│   Trend: ↓ 50% vs last month      │
├───────────────────────────────────┤
│ Secret Scanning                   │
│   Active Secrets: 0               │
│   Resolved This Month: 3          │
├───────────────────────────────────┤
│ Dependabot                        │
│   Critical: 0  High: 1            │
│   Average Resolution: 5 days      │
├───────────────────────────────────┤
│ Security Health: 95/100 ⭐        │
└───────────────────────────────────┘
```

### 5. Security Advisories

#### Creating Security Advisories

For reporting vulnerabilities to users:

```
Security → Advisories → New draft security advisory
```

**Fill in:**
- Ecosystem: NuGet
- Package: HeroMessaging
- Affected versions: < 1.2.3
- Patched versions: >= 1.2.3
- Severity: Critical / High / Medium / Low
- CWE: Select applicable weakness
- Description: Detailed vulnerability description
- Credits: Security researcher who reported it

**Publish Workflow:**
1. Draft advisory (private)
2. Request CVE (GitHub will assign)
3. Coordinate fix in private fork
4. Release fix
5. Publish advisory

#### Private Security Forks

For fixing undisclosed vulnerabilities:

```
Advisory → Create private fork
```

- Fork only visible to maintainers
- Develop fix privately
- Test without public exposure
- Merge when ready to disclose

## Advanced Configuration

### Custom Security Policies

Create `.github/SECURITY.md` ✅ (Already exists in our repo!)

Our file includes:
- Vulnerability reporting process
- Security features documentation
- Contact information
- Expected response times

### Security Contacts

Add security contacts:
```
Settings → Code security → Security contacts
→ Add email addresses
```

Contacts receive:
- Secret scanning alerts
- Dependabot alerts
- Security advisory notifications

### Webhook Integration

Send security alerts to external systems:

```
Settings → Webhooks → Add webhook
→ Payload URL: https://your-security-system.example.com/webhook
→ Events: Security and analysis
```

**Use Cases:**
- Send to SIEM (Splunk, Datadog)
- Notify Slack/Teams
- Trigger incident response
- Update ticketing systems

### API Integration

Automate security management:

```bash
# Get code scanning alerts
gh api repos/KoalaFacts/HeroMessaging/code-scanning/alerts

# Get secret scanning alerts
gh api repos/KoalaFacts/HeroMessaging/secret-scanning/alerts

# Get Dependabot alerts
gh api repos/KoalaFacts/HeroMessaging/dependabot/alerts

# Dismiss alert
gh api repos/KoalaFacts/HeroMessaging/code-scanning/alerts/123 \
  -X PATCH \
  -F state=dismissed \
  -F dismissed_reason=false_positive
```

## Best Practices

### Security Workflow

**Daily:**
- [ ] Review new secret scanning alerts (if any)
- [ ] Check for critical vulnerabilities

**Weekly:**
- [ ] Review CodeQL alerts
- [ ] Merge Dependabot security PRs
- [ ] Check security dashboard metrics

**Monthly:**
- [ ] Review security policy
- [ ] Update security contacts
- [ ] Review dismissed alerts
- [ ] Security training for team

### Alert Triage Process

1. **Assess Severity**
   - Critical: Fix immediately
   - High: Fix within 7 days
   - Medium: Fix within 30 days
   - Low: Fix when convenient

2. **Verify Exploitability**
   - Is vulnerable code actually used?
   - Are prerequisites met for exploitation?
   - What's the attack surface?

3. **Plan Remediation**
   - Update dependency?
   - Refactor code?
   - Add mitigations?
   - Accept risk with documentation?

4. **Track Progress**
   - Create issue for each alert
   - Assign to team member
   - Set due date based on severity
   - Link PRs to alerts

### Team Collaboration

**Assign Ownership:**
```
Code Scanning → Settings → Default assignees
→ Add team members for automatic assignment
```

**Security Champions:**
- Designate security champion in each team
- Champion reviews all security PRs
- Champion facilitates security training
- Champion escalates critical issues

## Compliance and Reporting

### Generating Reports

**Security Export:**
```bash
# Export all alerts
gh api repos/KoalaFacts/HeroMessaging/code-scanning/alerts \
  --paginate \
  --jq '.[] | {id, severity, rule: .rule.id, state}' \
  > security-report.json

# Generate CSV
gh api repos/KoalaFacts/HeroMessaging/code-scanning/alerts \
  --jq -r '.[] | [.id, .severity, .rule.id, .state, .created_at] | @csv' \
  > security-report.csv
```

**Compliance Reports:**
- SOC 2: Security alerts, resolution times
- ISO 27001: Security controls, audit trails
- PCI DSS: Vulnerability scanning evidence
- HIPAA: Access logs, security measures

### Metrics to Track

**KPIs:**
- Time to resolve critical alerts
- Number of open security alerts
- False positive rate
- Security test coverage
- Dependency update frequency

**Example Dashboard:**
```
Security KPIs (Q4 2025)
────────────────────────────
Mean Time to Resolve
  Critical: 4 hours ✅
  High: 2 days ✅
  Medium: 14 days ✅

Alert Volume
  Code Scanning: 12 (↓ 40%)
  Secret Scanning: 0
  Dependabot: 8 (↓ 20%)

Coverage
  Code Coverage: 85% ✅
  Dependency Updates: 95% ✅
```

## Cost Optimization

### For Private Repos

**Reduce Committer Count:**
- Only count active contributors (last 90 days)
- Use deploy keys instead of user accounts
- Use GitHub Apps for automation
- Consolidate bot accounts

**Example:**
```
Team: 10 developers
Bots: 3 (Dependabot, CI, Release)

GHAS Cost:
- 10 developers × $49 = $490/month
- 3 bots = FREE (not counted)

Optimization:
- Use 1 bot account instead of 3 = Same cost
- Remove inactive developers = Reduced cost
```

### Monitor Usage

```bash
# Check committer count
gh api /repos/KoalaFacts/HeroMessaging/code-scanning/analyses \
  --jq 'group_by(.commit_sha) | length'
```

## Troubleshooting

### Common Issues

**CodeQL fails to build:**
```
Error: Could not auto-detect a suitable build command
```
**Solution**: Add manual build step to workflow
```yaml
- name: Build
  run: dotnet build --configuration Release
```

---

**Secret scanning not working:**
```
Secret scanning is not enabled
```
**Solution**: Check repository is public or has GHAS license

---

**Dependabot PRs not creating:**
```
Dependabot is not configured
```
**Solution**: Enable in Settings → Code security or add `.github/dependabot.yml`

---

**Too many false positives:**
```
CodeQL flagging test code
```
**Solution**: Add paths to ignore in CodeQL config
```yaml
paths-ignore:
  - '**/Tests/**'
  - '**/TestData/**'
```

## Migration Guide

### From Other Tools

**From Snyk:**
- Export vulnerability data
- Map to GitHub alerts
- Configure Dependabot
- Migrate suppression rules

**From SonarQube:**
- Compare rule coverage
- Adjust CodeQL queries
- Map quality gates
- Export historical data

**From WhiteSource/Mend:**
- Export license data
- Configure dependency review
- Set up policy checks
- Migrate ignore lists

## Resources

### Documentation
- [GHAS Documentation](https://docs.github.com/en/get-started/learning-about-github/about-github-advanced-security)
- [CodeQL Documentation](https://codeql.github.com/docs/)
- [Secret Scanning Patterns](https://docs.github.com/en/code-security/secret-scanning/secret-scanning-patterns)

### Training
- [GitHub Security Lab](https://securitylab.github.com/)
- [CodeQL Training](https://codeql.github.com/docs/writing-codeql-queries/)
- [Secure Code Game](https://github.com/skills/secure-code-game)

### Community
- [GitHub Community](https://github.community/c/code-security/)
- [CodeQL Discussions](https://github.com/github/codeql/discussions)

## Summary

✅ **Workflows Ready**: CodeQL and security scanning configured
⚙️ **Requires Setup**: Enable in repository settings
🔐 **Features Available**: All GHAS features (if public or licensed)
📊 **Monitoring**: Security dashboard and alerts
📝 **Documentation**: Comprehensive guides included

Enable GHAS today for enterprise-grade security! 🛡️

---

**Last Updated**: 2025-11-07
**Version**: 1.0
**Applicable**: All repository types (public/private)
