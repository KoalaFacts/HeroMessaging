#!/bin/bash
# analyze-and-fix.sh - Systematic code quality improvement script
#
# Usage:
#   ./scripts/analyze-and-fix.sh [phase]
#
# Phases:
#   all        - Run all phases
#   security   - Fix security violations (CA2xxx, CA5xxx)
#   performance - Fix performance issues (CA18xx)
#   naming     - Fix naming violations
#   modern     - Apply modern C# patterns
#   report     - Generate analysis report only

set -e

PHASE="${1:-report}"
BUILD_LOG="build-analysis.log"
ERRORS_LOG="errors.log"
WARNINGS_LOG="warnings.log"

echo "🔍 HeroMessaging Code Quality Analysis"
echo "========================================"
echo ""

# Colors for output
RED='\033[0;31m'
YELLOW='\033[1;33m'
GREEN='\033[0;32m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Function to build and capture output
analyze_build() {
    echo -e "${BLUE}Building solution...${NC}"
    dotnet build --no-incremental /p:EnforceCodeStyleInBuild=true > "$BUILD_LOG" 2>&1 || true

    # Extract errors and warnings
    grep "error CA\|error CS\|error IDE" "$BUILD_LOG" > "$ERRORS_LOG" 2>/dev/null || echo "No errors found" > "$ERRORS_LOG"
    grep "warning CA\|warning CS\|warning IDE" "$BUILD_LOG" > "$WARNINGS_LOG" 2>/dev/null || echo "No warnings found" > "$WARNINGS_LOG"

    # Count violations
    ERROR_COUNT=$(grep -c "error" "$ERRORS_LOG" 2>/dev/null || echo 0)
    WARNING_COUNT=$(grep -c "warning" "$WARNINGS_LOG" 2>/dev/null || echo 0)

    echo -e "${RED}Errors: $ERROR_COUNT${NC}"
    echo -e "${YELLOW}Warnings: $WARNING_COUNT${NC}"
    echo ""
}

# Function to show top violations
show_top_violations() {
    echo -e "${BLUE}Top 10 Error Rules:${NC}"
    grep "error" "$ERRORS_LOG" | sed 's/.*\(CA[0-9]\{4\}\|CS[0-9]\{4\}\|IDE[0-9]\{4\}\).*/\1/' | sort | uniq -c | sort -rn | head -10
    echo ""

    echo -e "${BLUE}Top 10 Warning Rules:${NC}"
    grep "warning" "$WARNINGS_LOG" | sed 's/.*\(CA[0-9]\{4\}\|CS[0-9]\{4\}\|IDE[0-9]\{4\}\).*/\1/' | sort | uniq -c | sort -rn | head -10
    echo ""
}

# Function to fix security violations
fix_security() {
    echo -e "${RED}Fixing security violations...${NC}"

    # CA2100: SQL injection
    echo "  • Checking for SQL injection vulnerabilities..."
    grep -r "SqlCommand\|ExecuteReader\|ExecuteNonQuery" src/ --include="*.cs" || true

    # CA5350/CA5351: Weak crypto
    echo "  • Checking for weak cryptographic algorithms..."
    grep -r "MD5\|SHA1\|DES\|TripleDES" src/ --include="*.cs" || true

    # CA2326: Insecure JSON deserialization
    echo "  • Checking for insecure JSON settings..."
    grep -r "TypeNameHandling" src/ --include="*.cs" || true

    # CA2300: BinaryFormatter
    echo "  • Checking for BinaryFormatter usage..."
    grep -r "BinaryFormatter" src/ --include="*.cs" || true

    echo -e "${GREEN}  Security scan complete. Review output above.${NC}"
    echo ""
}

# Function to fix performance issues
fix_performance() {
    echo -e "${YELLOW}Fixing performance violations...${NC}"

    # This would run dotnet format with specific analyzers
    echo "  • Running performance optimizations..."
    dotnet format analyzers --diagnostics=CA1827,CA1829,CA1846,CA1851,CA1861 --verbosity=detailed || true

    echo -e "${GREEN}  Performance fixes applied.${NC}"
    echo ""
}

# Function to fix naming violations
fix_naming() {
    echo -e "${YELLOW}Fixing naming violations...${NC}"

    # This would run dotnet format with naming rules
    echo "  • Running naming convention fixes..."
    dotnet format analyzers --diagnostics=IDE1006 --verbosity=detailed || true

    echo -e "${GREEN}  Naming fixes applied.${NC}"
    echo ""
}

# Function to apply modern C# patterns
fix_modern() {
    echo -e "${YELLOW}Applying modern C# patterns...${NC}"

    # File-scoped namespaces
    echo "  • Converting to file-scoped namespaces..."
    dotnet format analyzers --diagnostics=IDE0160,IDE0161 --verbosity=detailed || true

    # Primary constructors (would need manual review)
    echo "  • Checking for primary constructor opportunities..."
    dotnet format analyzers --diagnostics=IDE0290 --verbosity=detailed || true

    # Collection expressions
    echo "  • Applying collection expression simplifications..."
    dotnet format analyzers --diagnostics=IDE0300,IDE0301,IDE0302,IDE0305 --verbosity=detailed || true

    echo -e "${GREEN}  Modern C# patterns applied.${NC}"
    echo ""
}

# Function to generate report
generate_report() {
    echo -e "${BLUE}Generating HTML report...${NC}"

    cat > code-quality-report.html <<EOF
<!DOCTYPE html>
<html>
<head>
    <title>HeroMessaging Code Quality Report</title>
    <style>
        body { font-family: 'Segoe UI', Tahoma, sans-serif; margin: 20px; background: #f5f5f5; }
        .container { max-width: 1200px; margin: 0 auto; background: white; padding: 30px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }
        h1 { color: #2c3e50; border-bottom: 3px solid #3498db; padding-bottom: 10px; }
        h2 { color: #34495e; margin-top: 30px; }
        .metric { display: inline-block; margin: 10px 20px 10px 0; padding: 15px; background: #ecf0f1; border-radius: 5px; }
        .error { color: #e74c3c; font-weight: bold; font-size: 24px; }
        .warning { color: #f39c12; font-weight: bold; font-size: 24px; }
        table { width: 100%; border-collapse: collapse; margin: 20px 0; }
        th { background: #3498db; color: white; padding: 12px; text-align: left; }
        td { padding: 10px; border-bottom: 1px solid #ddd; }
        tr:hover { background: #f8f9fa; }
        .rule-id { font-family: monospace; font-weight: bold; }
        .timestamp { color: #7f8c8d; font-size: 0.9em; }
    </style>
</head>
<body>
    <div class="container">
        <h1>🔍 HeroMessaging Code Quality Report</h1>
        <p class="timestamp">Generated: $(date)</p>

        <div class="metrics">
            <div class="metric">
                <div>Errors</div>
                <div class="error">$ERROR_COUNT</div>
            </div>
            <div class="metric">
                <div>Warnings</div>
                <div class="warning">$WARNING_COUNT</div>
            </div>
        </div>

        <h2>Top Error Rules</h2>
        <table>
            <tr><th>Count</th><th>Rule ID</th></tr>
EOF

    grep "error" "$ERRORS_LOG" | sed 's/.*\(CA[0-9]\{4\}\|CS[0-9]\{4\}\|IDE[0-9]\{4\}\).*/\1/' | sort | uniq -c | sort -rn | head -10 | while read count rule; do
        echo "<tr><td>$count</td><td class='rule-id'>$rule</td></tr>" >> code-quality-report.html
    done

    cat >> code-quality-report.html <<EOF
        </table>

        <h2>Top Warning Rules</h2>
        <table>
            <tr><th>Count</th><th>Rule ID</th></tr>
EOF

    grep "warning" "$WARNINGS_LOG" | sed 's/.*\(CA[0-9]\{4\}\|CS[0-9]\{4\}\|IDE[0-9]\{4\}\).*/\1/' | sort | uniq -c | sort -rn | head -10 | while read count rule; do
        echo "<tr><td>$count</td><td class='rule-id'>$rule</td></tr>" >> code-quality-report.html
    done

    cat >> code-quality-report.html <<EOF
        </table>

        <h2>Next Steps</h2>
        <ol>
            <li>Fix all errors (security and correctness)</li>
            <li>Run <code>./scripts/analyze-and-fix.sh security</code></li>
            <li>Run <code>./scripts/analyze-and-fix.sh performance</code></li>
            <li>Review naming violations and apply fixes</li>
        </ol>
    </div>
</body>
</html>
EOF

    echo -e "${GREEN}Report generated: code-quality-report.html${NC}"
    echo ""
}

# Main execution
case "$PHASE" in
    all)
        analyze_build
        show_top_violations
        fix_security
        fix_performance
        fix_naming
        fix_modern
        generate_report
        ;;
    security)
        analyze_build
        fix_security
        ;;
    performance)
        analyze_build
        fix_performance
        ;;
    naming)
        analyze_build
        fix_naming
        ;;
    modern)
        analyze_build
        fix_modern
        ;;
    report)
        analyze_build
        show_top_violations
        generate_report
        ;;
    *)
        echo "Unknown phase: $PHASE"
        echo "Usage: $0 [all|security|performance|naming|modern|report]"
        exit 1
        ;;
esac

echo -e "${GREEN}✅ Analysis complete!${NC}"
echo ""
echo "Logs:"
echo "  • Full build: $BUILD_LOG"
echo "  • Errors: $ERRORS_LOG"
echo "  • Warnings: $WARNINGS_LOG"
echo ""
echo "Next steps:"
echo "  1. Review the report: open code-quality-report.html"
echo "  2. Fix errors: ./scripts/analyze-and-fix.sh security"
echo "  3. Apply performance fixes: ./scripts/analyze-and-fix.sh performance"
