# NuGet Package Signing Setup Guide

Complete guide for setting up code signing for HeroMessaging NuGet packages.

## Overview

Package signing ensures that:
- ✅ Packages are authentic (from KoalaFacts)
- ✅ Packages haven't been tampered with
- ✅ Users can verify package integrity
- ✅ NuGet.org displays "Signed" badge

**Status**: ⚙️ Configured in workflow, requires certificate to activate

## Quick Start

### Option 1: Purchase Commercial Certificate (Recommended for Production)

**Providers:**
- [DigiCert](https://www.digicert.com/signing/code-signing-certificates) - $474/year
- [Sectigo](https://sectigo.com/ssl-certificates-tls/code-signing) - $415/year
- [GlobalSign](https://www.globalsign.com/en/code-signing-certificate) - $349/year

**Best for:**
- Production releases
- Public trust
- NuGet.org official signing
- Long-term projects

### Option 2: Self-Signed Certificate (For Testing)

**Best for:**
- Development testing
- Internal packages
- Learning the workflow
- Cost-free validation

**Limitations:**
- ❌ Not trusted by default
- ❌ No "Signed" badge on NuGet.org
- ❌ Users must manually trust certificate

## Step-by-Step Setup

### Method 1: Commercial Certificate (Production)

#### Step 1: Purchase Certificate

1. **Choose Provider**
   - Compare prices and validation requirements
   - Organization validation (OV) certificates recommended
   - Extended validation (EV) provides highest trust

2. **Complete Verification**
   - Provide business documentation
   - Verification typically takes 1-5 business days
   - You'll need:
     - Business registration documents
     - Domain ownership verification
     - Contact verification

3. **Receive Certificate**
   - Download certificate file (.pfx or .p12)
   - Securely store the password
   - Keep backup in secure location

#### Step 2: Prepare Certificate for GitHub

```bash
# Convert certificate to base64 for GitHub Secrets
base64 -w 0 your-certificate.pfx > certificate.base64

# The output file contains your base64-encoded certificate
cat certificate.base64
```

**Important**:
- Store the base64 file securely
- Never commit certificates to git
- Delete local copies after uploading to GitHub

#### Step 3: Add Secrets to GitHub

1. **Navigate to Repository Secrets**
   ```
   Repository → Settings → Secrets and variables → Actions → New repository secret
   ```

2. **Add Certificate Secret**
   - Name: `NUGET_SIGNING_CERT_BASE64`
   - Value: Paste entire contents of `certificate.base64`
   - Click "Add secret"

3. **Add Password Secret**
   - Name: `NUGET_SIGNING_CERT_PASSWORD`
   - Value: Your certificate password
   - Click "Add secret"

#### Step 4: Verify Configuration

The `create-release.yml` workflow will automatically:
- Detect the certificate secrets
- Decode the certificate
- Sign all .nupkg files
- Verify signatures
- Clean up certificate file

**Test with draft release:**
```bash
gh workflow run create-release.yml \
  -f version=0.0.1-test.1 \
  -f draft=true
```

Check workflow logs for:
```
🔐 Setting up code signing certificate...
Signing: HeroMessaging.0.0.1-test.1.nupkg
✅ Verified: HeroMessaging.0.0.1-test.1.nupkg
✅ Package signing complete
```

### Method 2: Self-Signed Certificate (Testing)

#### Step 1: Generate Self-Signed Certificate

**On Windows (PowerShell as Administrator):**
```powershell
# Create self-signed certificate
$cert = New-SelfSignedCertificate `
    -Subject "CN=KoalaFacts HeroMessaging Test" `
    -Type CodeSigningCert `
    -CertStoreLocation Cert:\CurrentUser\My `
    -NotAfter (Get-Date).AddYears(3)

# Export to PFX
$password = ConvertTo-SecureString -String "YourPassword123!" -Force -AsPlainText
Export-PfxCertificate `
    -Cert $cert `
    -FilePath "test-signing-cert.pfx" `
    -Password $password

Write-Host "Certificate created: test-signing-cert.pfx"
```

**On Linux/macOS (OpenSSL):**
```bash
# Generate private key
openssl genrsa -out test-key.pem 2048

# Generate self-signed certificate
openssl req -new -x509 -key test-key.pem -out test-cert.pem -days 1095 \
  -subj "/CN=KoalaFacts HeroMessaging Test/O=KoalaFacts/C=US"

# Combine into PFX
openssl pkcs12 -export -out test-signing-cert.pfx \
  -inkey test-key.pem -in test-cert.pem \
  -password pass:YourPassword123!

echo "Certificate created: test-signing-cert.pfx"
```

#### Step 2: Convert and Upload

Follow the same process as commercial certificates:
```bash
base64 -w 0 test-signing-cert.pfx > certificate.base64
```

Then add to GitHub Secrets as described above.

#### Step 3: Test Signing Locally

Before uploading to GitHub, test locally:

```bash
# Sign a test package
dotnet nuget sign ./test-package.nupkg \
  --certificate-path test-signing-cert.pfx \
  --certificate-password "YourPassword123!" \
  --timestamper http://timestamp.digicert.com

# Verify signature
dotnet nuget verify --all ./test-package.nupkg

# Expected output:
# Successfully verified package 'test-package.nupkg'.
```

## Workflow Integration

### Current Implementation

The signing happens in `create-release.yml`:

```yaml
- name: Sign NuGet packages
  if: secrets.NUGET_SIGNING_CERT_BASE64 != ''
  env:
    SIGNING_CERT_BASE64: ${{ secrets.NUGET_SIGNING_CERT_BASE64 }}
    SIGNING_CERT_PASSWORD: ${{ secrets.NUGET_SIGNING_CERT_PASSWORD }}
  run: |
    # Decode certificate
    echo "$SIGNING_CERT_BASE64" | base64 -d > signing-cert.pfx

    # Sign all packages
    for pkg in *.nupkg; do
      dotnet nuget sign "$pkg" \
        --certificate-path signing-cert.pfx \
        --certificate-password "$SIGNING_CERT_PASSWORD" \
        --timestamper http://timestamp.digicert.com
    done

    # Clean up
    rm -f signing-cert.pfx
```

### Security Features

✅ **Certificate Protection:**
- Certificate stored encrypted in GitHub Secrets
- Never logged or exposed in workflow output
- Automatically cleaned up after signing

✅ **Timestamping:**
- Uses DigiCert timestamp server
- Signature remains valid after certificate expires
- Provides long-term verification

✅ **Verification:**
- Automatic verification after signing
- Workflow fails if verification fails
- Ensures packages are correctly signed

## Verification for Users

### How Users Verify Signatures

**Before Installing:**
```bash
# Download package
nuget install HeroMessaging -OutputDirectory ./packages -NoCache

# Verify signature
dotnet nuget verify --all ./packages/HeroMessaging.*/HeroMessaging.*.nupkg
```

**Expected Output (Signed):**
```
Successfully verified package 'HeroMessaging.1.0.0.nupkg'.
Signature Hash Algorithm: SHA256
Signature Algorithm: sha256RSA
Timestamp: 2025-11-07 12:34:56 UTC
Certificate:
  Subject: CN=KoalaFacts
  Issuer: CN=DigiCert SHA2 Assured ID Code Signing CA
  Valid: 2024-01-01 to 2027-01-01
  Thumbprint: ABC123...
```

**Expected Output (Unsigned):**
```
warning NU3003: Package 'HeroMessaging.1.0.0.nupkg' does not contain a signature.
```

### Verifying on NuGet.org

1. Go to package page: https://www.nuget.org/packages/HeroMessaging
2. Look for **"Signed"** badge next to package name
3. Click badge to see certificate details
4. Verify certificate issuer matches expected (DigiCert, Sectigo, etc.)

## Certificate Management

### Certificate Expiration

Commercial certificates typically last 1-3 years:

**90 Days Before Expiration:**
- [ ] Order renewal from certificate provider
- [ ] Complete verification process
- [ ] Test new certificate locally

**30 Days Before Expiration:**
- [ ] Convert new certificate to base64
- [ ] Update GitHub secrets
- [ ] Test with draft release

**After Expiration:**
- Old packages remain signed (thanks to timestamping)
- New packages use new certificate
- Users don't need to take any action

### Certificate Renewal Process

```bash
# 1. Receive new certificate from provider
# 2. Convert to base64
base64 -w 0 new-certificate.pfx > new-cert.base64

# 3. Update GitHub Secrets
#    Repository → Settings → Secrets → NUGET_SIGNING_CERT_BASE64
#    Click "Update" and paste new base64 content
#    Update NUGET_SIGNING_CERT_PASSWORD if changed

# 4. Test signing with draft release
gh workflow run create-release.yml -f version=0.0.1-renewal-test -f draft=true

# 5. Verify in workflow logs
#    Check for successful signing with new certificate
```

### Certificate Revocation

If certificate is compromised:

1. **Immediate Actions:**
   ```bash
   # Remove secrets from GitHub
   gh secret remove NUGET_SIGNING_CERT_BASE64
   gh secret remove NUGET_SIGNING_CERT_PASSWORD
   ```

2. **Contact Certificate Authority:**
   - Request certificate revocation
   - Provide incident details
   - Follow their revocation process

3. **Obtain New Certificate:**
   - Purchase/generate new certificate
   - Use different key pair
   - Update GitHub secrets

4. **Notify Users:**
   - Create security advisory
   - Explain incident
   - Provide new certificate thumbprint

## Troubleshooting

### Common Issues

**Issue: Workflow skips signing**
```
ℹ️ Package signing skipped - no certificate configured
```
**Solution:** Add `NUGET_SIGNING_CERT_BASE64` and `NUGET_SIGNING_CERT_PASSWORD` secrets

---

**Issue: Invalid certificate password**
```
Error: The password is incorrect.
```
**Solution:**
- Verify password is correct
- Check for extra spaces in secret
- Re-create secret with correct password

---

**Issue: Certificate decode fails**
```
Error: base64: invalid input
```
**Solution:**
- Ensure base64 encoding used `-w 0` flag (no line wrapping)
- Re-encode: `base64 -w 0 cert.pfx > cert.base64`

---

**Issue: Signature verification fails**
```
error NU3018: The signature is invalid
```
**Solution:**
- Check certificate is valid code signing certificate
- Verify certificate hasn't expired
- Ensure timestamping succeeded

---

**Issue: NuGet.org doesn't show signed badge**
```
Package uploaded but no "Signed" badge
```
**Solution:**
- Self-signed certificates won't show badge
- Must use trusted CA-issued certificate
- Wait up to 30 minutes for badge to appear

## Cost Analysis

### Commercial Certificate

**One-Time Costs:**
- Certificate: $350-500/year
- Setup time: 2-4 hours

**Annual Costs:**
- Certificate renewal: $350-500/year
- Maintenance: 1 hour/year

**Total 3-Year Cost:** ~$1,050-1,500

### Self-Signed Certificate

**One-Time Costs:**
- Certificate generation: Free
- Setup time: 1-2 hours

**Annual Costs:**
- None (regenerate as needed)

**Total 3-Year Cost:** $0

### Recommendation

- **Open Source / Personal**: Self-signed acceptable
- **Commercial / Enterprise**: Purchase commercial certificate
- **High Trust Required**: Extended validation (EV) certificate

## Security Best Practices

✅ **Do:**
- Use strong certificate passwords (20+ characters)
- Store certificate securely (encrypted backup)
- Enable timestamping (signature survives expiration)
- Rotate certificates before expiration
- Monitor certificate validity

❌ **Don't:**
- Share certificate or password
- Commit certificates to git
- Use weak passwords
- Skip verification after signing
- Ignore expiration warnings

## References

- [NuGet Package Signing](https://docs.microsoft.com/nuget/create-packages/sign-a-package)
- [dotnet nuget sign](https://docs.microsoft.com/dotnet/core/tools/dotnet-nuget-sign)
- [dotnet nuget verify](https://docs.microsoft.com/dotnet/core/tools/dotnet-nuget-verify)
- [Code Signing Best Practices](https://github.com/ossf/wg-best-practices-os-developers/blob/main/docs/Concise-Guide-for-Developing-More-Secure-Software.md)

## Summary

✅ **Workflow**: Already configured in `create-release.yml`
⚙️ **Status**: Awaiting certificate secrets
🔐 **Benefit**: Package authenticity and integrity
📝 **Required**: Purchase certificate or generate self-signed

Once secrets are added, all releases will be automatically signed!

---

**Last Updated**: 2025-11-07
**Version**: 1.0
**Next Steps**: Add certificate secrets to activate signing
