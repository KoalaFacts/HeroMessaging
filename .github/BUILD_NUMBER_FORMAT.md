# Build Number Format

Documentation for the HeroMessaging build numbering scheme.

---

## 📋 Overview

HeroMessaging uses a structured build number format for CI-built packages that provides complete traceability, branch identification, and chronological ordering.

---

## 🏷️ Format

### CI Build Version Format

```
{base-version}-ci.{branch}.{date}.{run-number}.{git-hash}
```

### Components

| Component | Format | Example | Description |
|-----------|--------|---------|-------------|
| **base-version** | `X.Y.Z` | `1.0.0` | Semantic version from project file |
| **ci** | Literal | `ci` | Indicates CI build (not release) |
| **branch** | Sanitized name | `main`, `develop`, `release-1.0` | Git branch name (/ → -) |
| **date** | `YYYYMMDD` | `20251024` | UTC build date |
| **run-number** | Integer | `1234` | GitHub Actions run number (auto-incrementing) |
| **git-hash** | 7 chars | `abc123f` | Short commit SHA |

### Complete Example

```
1.0.0-ci.main.20251024.1234.abc123f
│     │  │    │        │    │
│     │  │    │        │    └─ Git commit (abc123f)
│     │  │    │        └────── Run #1234
│     │  │    └─────────────── Built on Oct 24, 2025
│     │  └──────────────────── From main branch
│     └─────────────────────── CI build marker
└───────────────────────────── Base version 1.0.0
```

---

## 🎯 Benefits

### 1. **Branch Identification**
- Instantly know which branch built the package
- Essential for multi-branch workflows
- Helps organize artifacts by branch

```bash
# Main branch builds
1.0.0-ci.main.20251024.1234.abc123f

# Develop branch builds
1.0.0-ci.develop.20251024.1235.def456a

# Release branch builds
1.0.0-ci.release-1.0.20251024.1236.789bcd2
```

### 2. **Chronological Ordering**
- Date prefix ensures packages sort by build date
- Easy to identify latest builds per branch
- Helps with debugging ("what changed between date X and Y?")

### 3. **Uniqueness**
- Branch + date + run number ensures no collisions
- Git hash provides exact source code reference
- Impossible to have duplicate versions

### 4. **Complete Traceability**
- Branch: Know which branch it was built from
- Date: Know when it was built
- Run number: Link to exact GitHub Actions workflow run
- Git hash: Know exact source code state

### 5. **Multi-Branch Support**
- Can build from multiple branches simultaneously
- Each branch has its own version stream
- Easy to filter by branch in artifact search

### 6. **NuGet Compatible**
- Follows semantic versioning with pre-release suffix
- NuGet understands `-ci` as pre-release tag
- Sorts correctly in package feeds

---

## 📊 Examples

### Different Branches

```bash
# Main branch
1.0.0-ci.main.20251024.1234.abc123f

# Develop branch
1.1.0-ci.develop.20251024.1235.def456a

# Feature branch (sanitized: feature/new-api → feature-new-api)
1.1.0-ci.feature-new-api.20251024.1236.789bcd2

# Release branch (sanitized: release/1.0.0 → release-1.0.0)
1.0.0-ci.release-1.0.0.20251024.1237.fedcba9
```

### Typical Progression (Main Branch)

```
1.0.0-ci.main.20251024.1234.abc123f  ← Morning build (run #1234)
1.0.0-ci.main.20251024.1235.def456a  ← Afternoon build (run #1235)
1.0.0-ci.main.20251025.1236.789bcd2  ← Next day (run #1236)
1.1.0-ci.main.20251026.1237.fedcba9  ← Version bump (run #1237)
```

### Comparison with Release Versions

```
CI Builds (main branch):
1.0.0-ci.main.20251024.1234.abc123f
1.0.0-ci.main.20251024.1235.def456a
1.0.0-ci.main.20251025.1236.789bcd2

Release:
1.0.0  ← Created from build main.20251024.1234.abc123f
```

### Multi-Branch Parallel Development

```
Main branch (stable):
1.0.0-ci.main.20251024.1234.abc123f

Develop branch (next version):
1.1.0-ci.develop.20251024.1235.def456a

Feature branch (experimental):
1.1.0-ci.feature-widget.20251024.1236.789bcd2

Release branch (preparing 1.0.1):
1.0.1-ci.release-1.0.1.20251024.1237.fedcba9
```

---

## 🔍 How to Decode a Build Number

Given: `1.2.3-ci.main.20251024.1456.a1b2c3d`

1. **Base Version**: `1.2.3`
   - Major: 1, Minor: 2, Patch: 3

2. **Branch**: `main`
   - Built from main branch
   - Could be: main, develop, feature-*, release-*, etc.

3. **Build Date**: `20251024`
   - Year: 2025
   - Month: 10 (October)
   - Day: 24

4. **Run Number**: `1456`
   - This was the 1,456th workflow run
   - Link: `https://github.com/KoalaFacts/HeroMessaging/actions/runs/{find-run-1456}`

5. **Git Commit**: `a1b2c3d`
   - Short SHA: a1b2c3d
   - Full commit: `https://github.com/KoalaFacts/HeroMessaging/commit/a1b2c3d`
   - View code: `git show a1b2c3d`

---

## 🔄 Conversion Process

### CI Build → Release

When creating a release, the CI build number is stripped:

```bash
# CI Build Package (from main branch)
HeroMessaging.1.0.0-ci.main.20251024.1234.abc123f.nupkg

# Renamed for Release
HeroMessaging.1.0.0.nupkg
```

**Process**:
1. Download CI package from artifacts
2. Extract build metadata (stored in release notes)
3. Rename package to release version
4. Upload to GitHub Release
5. Publish to NuGet.org

**Traceability Maintained**:
- Release notes include: "Built from main branch, CI run #1234"
- Link to exact workflow run
- Git commit tagged with release

---

## 🛠️ Implementation Details

### In ci.yml

```yaml
- name: Generate build number
  id: build-number
  run: |
    BASE_VERSION=$(grep -oP '<Version>\K[^<]+' src/HeroMessaging/HeroMessaging.csproj)

    # Extract and sanitize branch name
    BRANCH_NAME="${GITHUB_REF#refs/heads/}"
    BRANCH_NAME=$(echo "$BRANCH_NAME" | sed 's/\//-/g')

    BUILD_DATE=$(date -u +%Y%m%d)
    RUN_NUMBER=${{ github.run_number }}
    SHORT_SHA=${GITHUB_SHA:0:7}

    BUILD_NUMBER="${BRANCH_NAME}.${BUILD_DATE}.${RUN_NUMBER}.${SHORT_SHA}"
    FULL_VERSION="${BASE_VERSION}-ci.${BUILD_NUMBER}"

    echo "full-version=${FULL_VERSION}" >> $GITHUB_OUTPUT
```

### In create-release.yml

```yaml
- name: Rename packages with release version
  run: |
    # Match pattern: {package}-ci.{branch}.{date}.{run}.{sha}.{ext}
    if [[ "$file" =~ (.+)-ci\.[a-zA-Z0-9_-]+\.[0-9]+\.[0-9]+\.[a-f0-9]+\.(nupkg|snupkg)$ ]]; then
      base="${BASH_REMATCH[1]}"
      ext="${BASH_REMATCH[2]}"
      newname="${base}.${VERSION}.${ext}"
      mv "$file" "$newname"
    fi
```

---

## 📈 Use Cases

### 1. Finding Builds from Specific Branch

**Question**: "What was built from the develop branch?"

**Answer**:
```bash
# Search artifacts
gh run list --branch develop

# Look for packages with pattern
*-ci.develop.*
```

### 2. Finding Builds from Specific Date and Branch

**Question**: "What was built from main on October 24, 2025?"

**Answer**:
```bash
# Search for main branch builds from that date
*-ci.main.20251024.*

# Example results:
1.0.0-ci.main.20251024.1234.abc123f.nupkg
1.0.0-ci.main.20251024.1235.def456a.nupkg
```

### 2. Debugging a CI Build

**Question**: "Run #1234 failed, what was built?"

**Answer**:
```bash
# View run
gh run view 1234

# Download artifacts
gh run download 1234 --name release-packages-latest

# Inspect packages
ls -l
# Shows: HeroMessaging.1.0.0-ci.20251024.1234.abc123f.nupkg
```

### 3. Comparing Two Builds

**Question**: "What changed between runs #1234 and #1235?"

**Answer**:
```bash
# Extract git hashes from build numbers
# 1.0.0-ci.20251024.1234.abc123f → abc123f
# 1.0.0-ci.20251024.1235.def456a → def456a

# Compare commits
git diff abc123f..def456a
```

### 4. Reproducing a Build

**Question**: "Can I rebuild what was in run #1234?"

**Answer**:
```bash
# Get commit from build number
# 1.0.0-ci.20251024.1234.abc123f → abc123f

# Checkout exact commit
git checkout abc123f

# Build locally
dotnet build
dotnet pack
```

---

## 🔐 Security & Compliance

### Audit Requirements

The build number format satisfies compliance requirements:

1. **When**: Build date (YYYYMMDD)
2. **What**: Base version from project
3. **Where**: GitHub Actions run number
4. **Who**: Git commit author (in commit abc123f)
5. **How**: Workflow run logs

### Reproducibility

Given a build number, you can:
1. Find the exact source code (git hash)
2. View the exact build process (run number)
3. See all test results (workflow logs)
4. Reproduce the build (checkout commit + build)

---

## 📋 Quick Reference

### Format Pattern

```regex
^(\d+\.\d+\.\d+)-ci\.([a-zA-Z0-9_-]+)\.(\d{8})\.(\d+)\.([a-f0-9]{7})$
```

### Regex Groups

1. Base version (e.g., `1.0.0`)
2. Branch name (e.g., `main`, `develop`, `release-1.0`)
3. Date (e.g., `20251024`)
4. Run number (e.g., `1234`)
5. Git hash (e.g., `abc123f`)

### Example Parsing (Bash)

```bash
BUILD="1.0.0-ci.main.20251024.1234.abc123f"

if [[ "$BUILD" =~ ^([0-9.]+)-ci\.([a-zA-Z0-9_-]+)\.([0-9]{8})\.([0-9]+)\.([a-f0-9]{7})$ ]]; then
  VERSION="${BASH_REMATCH[1]}"    # 1.0.0
  BRANCH="${BASH_REMATCH[2]}"     # main
  DATE="${BASH_REMATCH[3]}"       # 20251024
  RUN="${BASH_REMATCH[4]}"        # 1234
  SHA="${BASH_REMATCH[5]}"        # abc123f
fi
```

---

## 🎓 Best Practices

### DO

✅ Use the full build number in CI artifacts
✅ Strip `-ci.*` suffix for releases
✅ Include build number in release notes
✅ Reference workflow run in documentation
✅ Keep artifacts for 90 days

### DON'T

❌ Don't publish CI builds to NuGet.org
❌ Don't reuse build numbers
❌ Don't modify packages after creation
❌ Don't delete workflow runs with associated releases
❌ Don't manually create build numbers

---

## 📞 Support

**Questions about build numbers?**
- See workflow logs: Actions → Cross-Platform Test Matrix
- Check artifacts: Actions → Workflow Run → Artifacts
- View releases: Releases tab

**Format changes?**
- Discuss in issue first
- Update this documentation
- Update workflows
- Test with draft release

---

**Last Updated**: 2025-10-24
**Maintained By**: Development Team
