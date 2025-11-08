# analyze-and-fix.ps1 - Systematic code quality improvement script for Windows
#
# Usage:
#   .\scripts\analyze-and-fix.ps1 [-Phase <phase>]
#
# Phases:
#   All        - Run all phases
#   Security   - Fix security violations (CA2xxx, CA5xxx)
#   Performance - Fix performance issues (CA18xx)
#   Naming     - Fix naming violations
#   Modern     - Apply modern C# patterns
#   Report     - Generate analysis report only (default)

param(
    [Parameter(Position=0)]
    [ValidateSet('All', 'Security', 'Performance', 'Naming', 'Modern', 'Report')]
    [string]$Phase = 'Report'
)

$ErrorActionPreference = 'Continue'
$BuildLog = "build-analysis.log"
$ErrorsLog = "errors.log"
$WarningsLog = "warnings.log"

Write-Host "🔍 HeroMessaging Code Quality Analysis" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

function Analyze-Build {
    Write-Host "Building solution..." -ForegroundColor Blue
    dotnet build --no-incremental /p:EnforceCodeStyleInBuild=true > $BuildLog 2>&1

    # Extract errors and warnings
    Get-Content $BuildLog | Select-String "error CA|error CS|error IDE" | Out-File $ErrorsLog
    Get-Content $BuildLog | Select-String "warning CA|warning CS|warning IDE" | Out-File $WarningsLog

    # Count violations
    $errorCount = (Get-Content $ErrorsLog -ErrorAction SilentlyContinue | Measure-Object -Line).Lines
    $warningCount = (Get-Content $WarningsLog -ErrorAction SilentlyContinue | Measure-Object -Line).Lines

    if ($errorCount -eq 0) { "No errors found" | Out-File $ErrorsLog }
    if ($warningCount -eq 0) { "No warnings found" | Out-File $WarningsLog }

    Write-Host "Errors: $errorCount" -ForegroundColor Red
    Write-Host "Warnings: $warningCount" -ForegroundColor Yellow
    Write-Host ""

    return @{
        ErrorCount = $errorCount
        WarningCount = $warningCount
    }
}

function Show-TopViolations {
    Write-Host "Top 10 Error Rules:" -ForegroundColor Blue
    Get-Content $ErrorsLog -ErrorAction SilentlyContinue |
        Select-String -Pattern '(CA\d{4}|CS\d{4}|IDE\d{4})' |
        ForEach-Object { $_.Matches.Value } |
        Group-Object |
        Sort-Object Count -Descending |
        Select-Object -First 10 |
        Format-Table Count, Name -AutoSize

    Write-Host "Top 10 Warning Rules:" -ForegroundColor Blue
    Get-Content $WarningsLog -ErrorAction SilentlyContinue |
        Select-String -Pattern '(CA\d{4}|CS\d{4}|IDE\d{4})' |
        ForEach-Object { $_.Matches.Value } |
        Group-Object |
        Sort-Object Count -Descending |
        Select-Object -First 10 |
        Format-Table Count, Name -AutoSize
}

function Fix-Security {
    Write-Host "Fixing security violations..." -ForegroundColor Red

    # CA2100: SQL injection
    Write-Host "  • Checking for SQL injection vulnerabilities..."
    Get-ChildItem -Path src\ -Recurse -Filter *.cs |
        Select-String -Pattern 'SqlCommand|ExecuteReader|ExecuteNonQuery' |
        Format-Table -AutoSize

    # CA5350/CA5351: Weak crypto
    Write-Host "  • Checking for weak cryptographic algorithms..."
    Get-ChildItem -Path src\ -Recurse -Filter *.cs |
        Select-String -Pattern 'MD5|SHA1|DES|TripleDES' |
        Format-Table -AutoSize

    # CA2326: Insecure JSON deserialization
    Write-Host "  • Checking for insecure JSON settings..."
    Get-ChildItem -Path src\ -Recurse -Filter *.cs |
        Select-String -Pattern 'TypeNameHandling' |
        Format-Table -AutoSize

    # CA2300: BinaryFormatter
    Write-Host "  • Checking for BinaryFormatter usage..."
    Get-ChildItem -Path src\ -Recurse -Filter *.cs |
        Select-String -Pattern 'BinaryFormatter' |
        Format-Table -AutoSize

    Write-Host "  Security scan complete. Review output above." -ForegroundColor Green
    Write-Host ""
}

function Fix-Performance {
    Write-Host "Fixing performance violations..." -ForegroundColor Yellow

    Write-Host "  • Running performance optimizations..."
    dotnet format analyzers --diagnostics=CA1827,CA1829,CA1846,CA1851,CA1861 --verbosity=detailed

    Write-Host "  Performance fixes applied." -ForegroundColor Green
    Write-Host ""
}

function Fix-Naming {
    Write-Host "Fixing naming violations..." -ForegroundColor Yellow

    Write-Host "  • Running naming convention fixes..."
    dotnet format analyzers --diagnostics=IDE1006 --verbosity=detailed

    Write-Host "  Naming fixes applied." -ForegroundColor Green
    Write-Host ""
}

function Fix-Modern {
    Write-Host "Applying modern C# patterns..." -ForegroundColor Yellow

    # File-scoped namespaces
    Write-Host "  • Converting to file-scoped namespaces..."
    dotnet format analyzers --diagnostics=IDE0160,IDE0161 --verbosity=detailed

    # Primary constructors
    Write-Host "  • Checking for primary constructor opportunities..."
    dotnet format analyzers --diagnostics=IDE0290 --verbosity=detailed

    # Collection expressions
    Write-Host "  • Applying collection expression simplifications..."
    dotnet format analyzers --diagnostics=IDE0300,IDE0301,IDE0302,IDE0305 --verbosity=detailed

    Write-Host "  Modern C# patterns applied." -ForegroundColor Green
    Write-Host ""
}

function Generate-Report {
    param($Metrics)

    Write-Host "Generating HTML report..." -ForegroundColor Blue

    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $errorCount = $Metrics.ErrorCount
    $warningCount = $Metrics.WarningCount

    # Get top error rules
    $topErrors = Get-Content $ErrorsLog -ErrorAction SilentlyContinue |
        Select-String -Pattern '(CA\d{4}|CS\d{4}|IDE\d{4})' |
        ForEach-Object { $_.Matches.Value } |
        Group-Object |
        Sort-Object Count -Descending |
        Select-Object -First 10

    # Get top warning rules
    $topWarnings = Get-Content $WarningsLog -ErrorAction SilentlyContinue |
        Select-String -Pattern '(CA\d{4}|CS\d{4}|IDE\d{4})' |
        ForEach-Object { $_.Matches.Value } |
        Group-Object |
        Sort-Object Count -Descending |
        Select-Object -First 10

    $html = @"
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
        ol { line-height: 1.8; }
        code { background: #f4f4f4; padding: 2px 6px; border-radius: 3px; }
    </style>
</head>
<body>
    <div class="container">
        <h1>🔍 HeroMessaging Code Quality Report</h1>
        <p class="timestamp">Generated: $timestamp</p>

        <div class="metrics">
            <div class="metric">
                <div>Errors</div>
                <div class="error">$errorCount</div>
            </div>
            <div class="metric">
                <div>Warnings</div>
                <div class="warning">$warningCount</div>
            </div>
        </div>

        <h2>Top Error Rules</h2>
        <table>
            <tr><th>Count</th><th>Rule ID</th></tr>
"@

    foreach ($rule in $topErrors) {
        $html += "            <tr><td>$($rule.Count)</td><td class='rule-id'>$($rule.Name)</td></tr>`n"
    }

    $html += @"
        </table>

        <h2>Top Warning Rules</h2>
        <table>
            <tr><th>Count</th><th>Rule ID</th></tr>
"@

    foreach ($rule in $topWarnings) {
        $html += "            <tr><td>$($rule.Count)</td><td class='rule-id'>$($rule.Name)</td></tr>`n"
    }

    $html += @"
        </table>

        <h2>Next Steps</h2>
        <ol>
            <li>Fix all errors (security and correctness)</li>
            <li>Run <code>.\scripts\analyze-and-fix.ps1 -Phase Security</code></li>
            <li>Run <code>.\scripts\analyze-and-fix.ps1 -Phase Performance</code></li>
            <li>Review naming violations and apply fixes</li>
            <li>Consider modern C# patterns: <code>.\scripts\analyze-and-fix.ps1 -Phase Modern</code></li>
        </ol>

        <h2>Documentation</h2>
        <ul>
            <li><a href="docs/code-quality-improvement-plan.md">Code Quality Improvement Plan</a></li>
            <li><a href="CLAUDE.md">Development Guidelines</a></li>
            <li><a href=".editorconfig">EditorConfig Settings</a></li>
        </ul>
    </div>
</body>
</html>
"@

    $html | Out-File "code-quality-report.html" -Encoding UTF8
    Write-Host "Report generated: code-quality-report.html" -ForegroundColor Green
    Write-Host ""
}

# Main execution
$metrics = Analyze-Build

switch ($Phase) {
    'All' {
        Show-TopViolations
        Fix-Security
        Fix-Performance
        Fix-Naming
        Fix-Modern
        Generate-Report -Metrics $metrics
    }
    'Security' {
        Fix-Security
    }
    'Performance' {
        Fix-Performance
    }
    'Naming' {
        Fix-Naming
    }
    'Modern' {
        Fix-Modern
    }
    'Report' {
        Show-TopViolations
        Generate-Report -Metrics $metrics
    }
}

Write-Host "✅ Analysis complete!" -ForegroundColor Green
Write-Host ""
Write-Host "Logs:"
Write-Host "  • Full build: $BuildLog"
Write-Host "  • Errors: $ErrorsLog"
Write-Host "  • Warnings: $WarningsLog"
Write-Host ""
Write-Host "Next steps:"
Write-Host "  1. Review the report: code-quality-report.html"
Write-Host "  2. Fix errors: .\scripts\analyze-and-fix.ps1 -Phase Security"
Write-Host "  3. Apply performance fixes: .\scripts\analyze-and-fix.ps1 -Phase Performance"
