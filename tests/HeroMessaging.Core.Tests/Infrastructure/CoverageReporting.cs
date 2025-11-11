using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;

namespace HeroMessaging.Tests.Infrastructure;

/// <summary>
/// Comprehensive coverage reporting with ReportGenerator integration and threshold enforcement
/// Provides detailed coverage analysis, trend tracking, and quality gate enforcement
/// </summary>
internal class CoverageReporting
{
    private readonly CoverageConfiguration _config;
    private const double MinimumCoverageThreshold = 80.0;

    public CoverageReporting(CoverageConfiguration? config = null)
    {
        _config = config ?? new CoverageConfiguration();
    }

    /// <summary>
    /// Generates comprehensive coverage reports from collected coverage data
    /// </summary>
    /// <param name="coverageFiles">Collection of coverage data files</param>
    /// <param name="outputDirectory">Directory for generated reports</param>
    /// <returns>Coverage report results</returns>
    public async Task<CoverageReportResults> GenerateReportsAsync(
        IEnumerable<string> coverageFiles,
        string outputDirectory)
    {
        var results = new CoverageReportResults
        {
            StartTime = DateTimeOffset.UtcNow,
            OutputDirectory = outputDirectory
        };

        try
        {
            Directory.CreateDirectory(outputDirectory);

            // Validate input files
            var validFiles = ValidateCoverageFiles(coverageFiles);
            if (!validFiles.Any())
            {
                results.Success = false;
                results.ErrorMessage = "No valid coverage files found";
                return results;
            }

            // Parse coverage data
            var coverageData = await ParseCoverageDataAsync(validFiles);
            results.CoverageData = coverageData;

            // Generate HTML report using ReportGenerator
            var htmlReportPath = await GenerateHtmlReportAsync(validFiles, outputDirectory);
            results.HtmlReportPath = htmlReportPath;

            // Generate badges
            var badgesPath = await GenerateBadgesAsync(coverageData, outputDirectory);
            results.BadgesPath = badgesPath;

            // Generate summary report
            var summaryPath = await GenerateSummaryReportAsync(coverageData, outputDirectory);
            results.SummaryReportPath = summaryPath;

            // Generate trend analysis if historical data exists
            var trendAnalysis = await GenerateTrendAnalysisAsync(coverageData, outputDirectory);
            results.TrendAnalysis = trendAnalysis;

            // Validate coverage thresholds
            var thresholdValidation = ValidateCoverageThresholds(coverageData);
            results.ThresholdValidation = thresholdValidation;

            // Generate diff coverage if in PR context
            if (_config.GenerateDiffCoverage && !string.IsNullOrEmpty(_config.BaselineDirectory))
            {
                var diffCoverage = await GenerateDiffCoverageAsync(coverageData, _config.BaselineDirectory, outputDirectory);
                results.DiffCoverage = diffCoverage;
            }

            results.Success = thresholdValidation.PassesMinimumThreshold;
        }
        catch (Exception ex)
        {
            results.Success = false;
            results.ErrorMessage = ex.Message;
        }
        finally
        {
            results.EndTime = DateTimeOffset.UtcNow;
            results.Duration = results.EndTime - results.StartTime;
        }

        return results;
    }

    /// <summary>
    /// Validates coverage thresholds against constitutional requirements
    /// </summary>
    /// <param name="coverageData">Coverage data to validate</param>
    /// <returns>Threshold validation results</returns>
    public CoverageThresholdValidation ValidateCoverageThresholds(CoverageData coverageData)
    {
        var validation = new CoverageThresholdValidation();

        try
        {
            // Overall coverage validation
            validation.OverallCoveragePercent = coverageData.OverallCoverage.LinePercentage;
            validation.PassesMinimumThreshold = validation.OverallCoveragePercent >= MinimumCoverageThreshold;

            // Per-assembly validation
            foreach (var assembly in coverageData.Assemblies)
            {
                var assemblyValidation = new AssemblyCoverageValidation
                {
                    AssemblyName = assembly.Name,
                    CoveragePercent = assembly.LinePercentage,
                    PassesThreshold = assembly.LinePercentage >= _config.MinimumAssemblyCoverage
                };

                // Public API coverage should be 100%
                var publicApiCoverage = CalculatePublicApiCoverage(assembly);
                assemblyValidation.PublicApiCoveragePercent = publicApiCoverage;
                assemblyValidation.PublicApiPassesThreshold = publicApiCoverage >= _config.MinimumPublicApiCoverage;

                validation.AssemblyValidations.Add(assemblyValidation);
            }

            // Critical path validation
            var criticalPaths = IdentifyCriticalPaths(coverageData);
            foreach (var path in criticalPaths)
            {
                var criticalValidation = new CriticalPathValidation
                {
                    Path = path.Path,
                    CoveragePercent = path.CoveragePercent,
                    PassesThreshold = path.CoveragePercent >= _config.MinimumCriticalPathCoverage
                };

                validation.CriticalPathValidations.Add(criticalValidation);
            }

            validation.Success = validation.PassesMinimumThreshold &&
                               validation.AssemblyValidations.All(a => a.PassesThreshold) &&
                               validation.CriticalPathValidations.All(c => c.PassesThreshold);
        }
        catch (Exception ex)
        {
            validation.Success = false;
            validation.ErrorMessage = ex.Message;
        }

        return validation;
    }

    /// <summary>
    /// Generates coverage trend analysis from historical data
    /// </summary>
    /// <param name="currentCoverage">Current coverage data</param>
    /// <param name="outputDirectory">Output directory for trend report</param>
    /// <returns>Coverage trend analysis</returns>
    public async Task<CoverageTrendAnalysis> GenerateTrendAnalysisAsync(
        CoverageData currentCoverage,
        string outputDirectory)
    {
        var trendAnalysis = new CoverageTrendAnalysis
        {
            CurrentCoverage = currentCoverage.OverallCoverage.LinePercentage,
            AnalysisDate = DateTimeOffset.UtcNow
        };

        try
        {
            var historicalDataPath = Path.Combine(outputDirectory, "coverage-history.json");

            // Load historical data
            var historicalData = await LoadHistoricalCoverageDataAsync(historicalDataPath);

            // Add current data point
            historicalData.Add(new CoverageDataPoint
            {
                Date = DateTimeOffset.UtcNow,
                OverallCoverage = currentCoverage.OverallCoverage.LinePercentage,
                AssemblyCoverages = currentCoverage.Assemblies.ToDictionary(
                    a => a.Name,
                    a => a.LinePercentage)
            });

            // Analyze trends
            if (historicalData.Count >= 2)
            {
                var previousCoverage = historicalData[historicalData.Count - 2].OverallCoverage;
                trendAnalysis.PreviousCoverage = previousCoverage;
                trendAnalysis.CoverageChange = trendAnalysis.CurrentCoverage - previousCoverage;

                // Determine trend direction
                if (Math.Abs(trendAnalysis.CoverageChange) < 0.1)
                {
                    trendAnalysis.TrendDirection = CoverageTrendDirection.Stable;
                }
                else if (trendAnalysis.CoverageChange > 0)
                {
                    trendAnalysis.TrendDirection = CoverageTrendDirection.Improving;
                }
                else
                {
                    trendAnalysis.TrendDirection = CoverageTrendDirection.Declining;
                }

                // Calculate moving average if we have enough data points
                if (historicalData.Count >= 5)
                {
                    var last5Points = historicalData.TakeLast(5).Select(d => d.OverallCoverage);
                    trendAnalysis.MovingAverage = last5Points.Average();
                }
            }

            // Save updated historical data
            await SaveHistoricalCoverageDataAsync(historicalData, historicalDataPath);

            // Generate trend chart data
            trendAnalysis.ChartData = GenerateTrendChartData(historicalData);
        }
        catch (Exception ex)
        {
            trendAnalysis.ErrorMessage = ex.Message;
        }

        return trendAnalysis;
    }

    /// <summary>
    /// Generates diff coverage report for pull request validation
    /// </summary>
    /// <param name="currentCoverage">Current coverage data</param>
    /// <param name="baselineDirectory">Baseline coverage directory</param>
    /// <param name="outputDirectory">Output directory</param>
    /// <returns>Diff coverage analysis</returns>
    public async Task<DiffCoverageAnalysis> GenerateDiffCoverageAsync(
        CoverageData currentCoverage,
        string baselineDirectory,
        string outputDirectory)
    {
        var diffAnalysis = new DiffCoverageAnalysis
        {
            AnalysisDate = DateTimeOffset.UtcNow
        };

        try
        {
            // Load baseline coverage data
            var baselineCoverageFile = Directory.GetFiles(baselineDirectory, "*.cobertura.xml").FirstOrDefault();
            if (baselineCoverageFile == null)
            {
                diffAnalysis.ErrorMessage = "No baseline coverage file found";
                return diffAnalysis;
            }

            var baselineCoverage = await ParseCoverageDataAsync(new[] { baselineCoverageFile });

            // Compare overall coverage
            diffAnalysis.BaselineCoverage = baselineCoverage.OverallCoverage.LinePercentage;
            diffAnalysis.CurrentCoverage = currentCoverage.OverallCoverage.LinePercentage;
            diffAnalysis.CoverageChange = diffAnalysis.CurrentCoverage - diffAnalysis.BaselineCoverage;

            // Compare assembly-level coverage
            foreach (var currentAssembly in currentCoverage.Assemblies)
            {
                var baselineAssembly = baselineCoverage.Assemblies.FirstOrDefault(a => a.Name == currentAssembly.Name);
                if (baselineAssembly != null)
                {
                    var assemblyDiff = new AssemblyCoverageDiff
                    {
                        AssemblyName = currentAssembly.Name,
                        BaselineCoverage = baselineAssembly.LinePercentage,
                        CurrentCoverage = currentAssembly.LinePercentage,
                        CoverageChange = currentAssembly.LinePercentage - baselineAssembly.LinePercentage
                    };

                    diffAnalysis.AssemblyDiffs.Add(assemblyDiff);
                }
            }

            // Identify significant changes (> 1% change)
            diffAnalysis.SignificantChanges = diffAnalysis.AssemblyDiffs
                .Where(d => Math.Abs(d.CoverageChange) > 1.0)
                .ToList();

            // Generate diff report
            var diffReportPath = Path.Combine(outputDirectory, "diff-coverage-report.md");
            await GenerateDiffReportMarkdownAsync(diffAnalysis, diffReportPath);
            diffAnalysis.ReportPath = diffReportPath;

            diffAnalysis.Success = true;
        }
        catch (Exception ex)
        {
            diffAnalysis.Success = false;
            diffAnalysis.ErrorMessage = ex.Message;
        }

        return diffAnalysis;
    }

    private IEnumerable<string> ValidateCoverageFiles(IEnumerable<string> coverageFiles)
    {
        var validFiles = new List<string>();

        foreach (var file in coverageFiles)
        {
            if (File.Exists(file) && (file.EndsWith(".cobertura.xml") || file.EndsWith(".opencover.xml")))
            {
                validFiles.Add(file);
            }
        }

        return validFiles;
    }

    private async Task<CoverageData> ParseCoverageDataAsync(IEnumerable<string> coverageFiles)
    {
        var coverageData = new CoverageData();
        var allAssemblies = new List<AssemblyCoverage>();

        foreach (var file in coverageFiles)
        {
            try
            {
                var assemblies = await ParseCoberturaFileAsync(file);
                allAssemblies.AddRange(assemblies);
            }
            catch (Exception ex)
            {
                // Log warning but continue with other files
                Console.WriteLine($"Warning: Failed to parse coverage file {file}: {ex.Message}");
            }
        }

        // Merge assemblies with same name
        var mergedAssemblies = MergeAssemblyCoverage(allAssemblies);
        coverageData.Assemblies = mergedAssemblies;

        // Calculate overall coverage
        coverageData.OverallCoverage = CalculateOverallCoverage(mergedAssemblies);

        return coverageData;
    }

    private async Task<List<AssemblyCoverage>> ParseCoberturaFileAsync(string filePath)
    {
        var assemblies = new List<AssemblyCoverage>();

        var xml = await File.ReadAllTextAsync(filePath);
        var doc = XDocument.Parse(xml);

        var packagesElement = doc.Descendants("packages").FirstOrDefault();
        if (packagesElement == null) return assemblies;

        foreach (var package in packagesElement.Elements("package"))
        {
            var assemblyName = package.Attribute("name")?.Value ?? "Unknown";
            var lineRate = double.Parse(package.Attribute("line-rate")?.Value ?? "0");
            var branchRate = double.Parse(package.Attribute("branch-rate")?.Value ?? "0");

            var assembly = new AssemblyCoverage
            {
                Name = assemblyName,
                LinePercentage = lineRate * 100,
                BranchPercentage = branchRate * 100
            };

            // Parse classes
            var classesElement = package.Element("classes");
            if (classesElement != null)
            {
                foreach (var classElement in classesElement.Elements("class"))
                {
                    var className = classElement.Attribute("name")?.Value ?? "Unknown";
                    var classLineRate = double.Parse(classElement.Attribute("line-rate")?.Value ?? "0");
                    var classBranchRate = double.Parse(classElement.Attribute("branch-rate")?.Value ?? "0");

                    var classCoverage = new ClassCoverage
                    {
                        Name = className,
                        LinePercentage = classLineRate * 100,
                        BranchPercentage = classBranchRate * 100
                    };

                    assembly.Classes.Add(classCoverage);
                }
            }

            assemblies.Add(assembly);
        }

        return assemblies;
    }

    private List<AssemblyCoverage> MergeAssemblyCoverage(List<AssemblyCoverage> assemblies)
    {
        return assemblies
            .GroupBy(a => a.Name)
            .Select(g => new AssemblyCoverage
            {
                Name = g.Key,
                LinePercentage = g.Average(a => a.LinePercentage),
                BranchPercentage = g.Average(a => a.BranchPercentage),
                Classes = g.SelectMany(a => a.Classes).ToList()
            })
            .ToList();
    }

    private CoverageMetrics CalculateOverallCoverage(List<AssemblyCoverage> assemblies)
    {
        if (!assemblies.Any())
        {
            return new CoverageMetrics();
        }

        return new CoverageMetrics
        {
            LinePercentage = assemblies.Average(a => a.LinePercentage),
            BranchPercentage = assemblies.Average(a => a.BranchPercentage)
        };
    }

    private async Task<string> GenerateHtmlReportAsync(IEnumerable<string> coverageFiles, string outputDirectory)
    {
        var htmlOutputPath = Path.Combine(outputDirectory, "html");
        Directory.CreateDirectory(htmlOutputPath);

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "reportgenerator",
                Arguments = $"-reports:\"{string.Join(";", coverageFiles)}\" " +
                          $"-targetdir:\"{htmlOutputPath}\" " +
                          $"-reporttypes:Html " +
                          $"-classfilters:\"-System.*;-Microsoft.*;-*.Tests.*\" " +
                          $"-assemblyfilters:\"+HeroMessaging.*;-*.Tests\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"ReportGenerator failed: {error}");
        }

        return Path.Combine(htmlOutputPath, "index.html");
    }

    private async Task<string> GenerateBadgesAsync(CoverageData coverageData, string outputDirectory)
    {
        var badgesOutputPath = Path.Combine(outputDirectory, "badges");
        Directory.CreateDirectory(badgesOutputPath);

        // Generate coverage badge using ReportGenerator
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "reportgenerator",
                Arguments = $"-reports:\"{Path.Combine(outputDirectory, "*.cobertura.xml")}\" " +
                          $"-targetdir:\"{badgesOutputPath}\" " +
                          $"-reporttypes:Badges",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        await process.WaitForExitAsync();

        return badgesOutputPath;
    }

    private async Task<string> GenerateSummaryReportAsync(CoverageData coverageData, string outputDirectory)
    {
        var summaryPath = Path.Combine(outputDirectory, "coverage-summary.md");

        var summary = new List<string>
        {
            "# Code Coverage Summary",
            "",
            $"**Overall Coverage:** {coverageData.OverallCoverage.LinePercentage:F1}%",
            $"**Branch Coverage:** {coverageData.OverallCoverage.BranchPercentage:F1}%",
            $"**Generated:** {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC",
            "",
            "## Assembly Coverage",
            ""
        };

        summary.Add("| Assembly | Line Coverage | Branch Coverage |");
        summary.Add("|----------|---------------|-----------------|");

        foreach (var assembly in coverageData.Assemblies.OrderByDescending(a => a.LinePercentage))
        {
            var lineIcon = assembly.LinePercentage >= MinimumCoverageThreshold ? "✅" : "❌";
            summary.Add($"| {lineIcon} {assembly.Name} | {assembly.LinePercentage:F1}% | {assembly.BranchPercentage:F1}% |");
        }

        await File.WriteAllTextAsync(summaryPath, string.Join(Environment.NewLine, summary));
        return summaryPath;
    }

    private double CalculatePublicApiCoverage(AssemblyCoverage assembly)
    {
        // Simplified implementation - in practice, would analyze IL metadata
        var publicClasses = assembly.Classes.Where(c => !c.Name.Contains("Internal") && !c.Name.Contains("Private"));
        return publicClasses.Any() ? publicClasses.Average(c => c.LinePercentage) : 0;
    }

    private List<CriticalPath> IdentifyCriticalPaths(CoverageData coverageData)
    {
        // Simplified implementation - would use actual critical path analysis
        var criticalPaths = new List<CriticalPath>();

        foreach (var assembly in coverageData.Assemblies)
        {
            var coreClasses = assembly.Classes.Where(c =>
                c.Name.Contains("Processor") ||
                c.Name.Contains("Handler") ||
                c.Name.Contains("Manager"));

            foreach (var coreClass in coreClasses)
            {
                criticalPaths.Add(new CriticalPath
                {
                    Path = $"{assembly.Name}.{coreClass.Name}",
                    CoveragePercent = coreClass.LinePercentage
                });
            }
        }

        return criticalPaths;
    }

    private async Task<List<CoverageDataPoint>> LoadHistoricalCoverageDataAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new List<CoverageDataPoint>();
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            return JsonSerializer.Deserialize<List<CoverageDataPoint>>(json) ?? new List<CoverageDataPoint>();
        }
        catch
        {
            return new List<CoverageDataPoint>();
        }
    }

    private async Task SaveHistoricalCoverageDataAsync(List<CoverageDataPoint> data, string filePath)
    {
        // Keep only last 100 data points
        var dataToSave = data.TakeLast(100).ToList();

        var json = JsonSerializer.Serialize(dataToSave, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(filePath, json);
    }

    private TrendChartData GenerateTrendChartData(List<CoverageDataPoint> historicalData)
    {
        return new TrendChartData
        {
            Labels = historicalData.Select(d => d.Date.ToString("yyyy-MM-dd")).ToList(),
            CoverageValues = historicalData.Select(d => d.OverallCoverage).ToList()
        };
    }

    private async Task GenerateDiffReportMarkdownAsync(DiffCoverageAnalysis diffAnalysis, string filePath)
    {
        var report = new List<string>
        {
            "# Coverage Diff Report",
            "",
            $"**Overall Coverage Change:** {diffAnalysis.CoverageChange:+0.0;-0.0;0.0}% " +
            $"({diffAnalysis.BaselineCoverage:F1}% → {diffAnalysis.CurrentCoverage:F1}%)",
            ""
        };

        if (diffAnalysis.SignificantChanges.Any())
        {
            report.Add("## Significant Changes (>1%)");
            report.Add("");
            report.Add("| Assembly | Baseline | Current | Change |");
            report.Add("|----------|----------|---------|--------|");

            foreach (var change in diffAnalysis.SignificantChanges)
            {
                var icon = change.CoverageChange > 0 ? "📈" : "📉";
                report.Add($"| {icon} {change.AssemblyName} | {change.BaselineCoverage:F1}% | {change.CurrentCoverage:F1}% | {change.CoverageChange:+0.0;-0.0;0.0}% |");
            }
        }
        else
        {
            report.Add("✅ No significant coverage changes detected.");
        }

        await File.WriteAllTextAsync(filePath, string.Join(Environment.NewLine, report));
    }
}

// Supporting data structures

internal class CoverageConfiguration
{
    public double MinimumAssemblyCoverage { get; set; } = 80.0;
    public double MinimumPublicApiCoverage { get; set; } = 100.0;
    public double MinimumCriticalPathCoverage { get; set; } = 95.0;
    public bool GenerateDiffCoverage { get; set; } = true;
    public string? BaselineDirectory { get; set; }
}

internal class CoverageReportResults
{
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public TimeSpan Duration { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string OutputDirectory { get; set; } = string.Empty;
    public string? HtmlReportPath { get; set; }
    public string? BadgesPath { get; set; }
    public string? SummaryReportPath { get; set; }
    public CoverageData? CoverageData { get; set; }
    public CoverageThresholdValidation? ThresholdValidation { get; set; }
    public CoverageTrendAnalysis? TrendAnalysis { get; set; }
    public DiffCoverageAnalysis? DiffCoverage { get; set; }
}

internal class CoverageData
{
    public CoverageMetrics OverallCoverage { get; set; } = new();
    public List<AssemblyCoverage> Assemblies { get; set; } = new();
}

internal class CoverageMetrics
{
    public double LinePercentage { get; set; }
    public double BranchPercentage { get; set; }
}

internal class AssemblyCoverage
{
    public string Name { get; set; } = string.Empty;
    public double LinePercentage { get; set; }
    public double BranchPercentage { get; set; }
    public List<ClassCoverage> Classes { get; set; } = new();
}

internal class ClassCoverage
{
    public string Name { get; set; } = string.Empty;
    public double LinePercentage { get; set; }
    public double BranchPercentage { get; set; }
}

internal class CoverageThresholdValidation
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public double OverallCoveragePercent { get; set; }
    public bool PassesMinimumThreshold { get; set; }
    public List<AssemblyCoverageValidation> AssemblyValidations { get; set; } = new();
    public List<CriticalPathValidation> CriticalPathValidations { get; set; } = new();
}

internal class AssemblyCoverageValidation
{
    public string AssemblyName { get; set; } = string.Empty;
    public double CoveragePercent { get; set; }
    public bool PassesThreshold { get; set; }
    public double PublicApiCoveragePercent { get; set; }
    public bool PublicApiPassesThreshold { get; set; }
}

internal class CriticalPathValidation
{
    public string Path { get; set; } = string.Empty;
    public double CoveragePercent { get; set; }
    public bool PassesThreshold { get; set; }
}

internal class CriticalPath
{
    public string Path { get; set; } = string.Empty;
    public double CoveragePercent { get; set; }
}

internal class CoverageTrendAnalysis
{
    public double CurrentCoverage { get; set; }
    public double PreviousCoverage { get; set; }
    public double CoverageChange { get; set; }
    public double MovingAverage { get; set; }
    public CoverageTrendDirection TrendDirection { get; set; }
    public DateTime AnalysisDate { get; set; }
    public TrendChartData? ChartData { get; set; }
    public string? ErrorMessage { get; set; }
}

internal class DiffCoverageAnalysis
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime AnalysisDate { get; set; }
    public double BaselineCoverage { get; set; }
    public double CurrentCoverage { get; set; }
    public double CoverageChange { get; set; }
    public List<AssemblyCoverageDiff> AssemblyDiffs { get; set; } = new();
    public List<AssemblyCoverageDiff> SignificantChanges { get; set; } = new();
    public string? ReportPath { get; set; }
}

internal class AssemblyCoverageDiff
{
    public string AssemblyName { get; set; } = string.Empty;
    public double BaselineCoverage { get; set; }
    public double CurrentCoverage { get; set; }
    public double CoverageChange { get; set; }
}

internal class CoverageDataPoint
{
    public DateTime Date { get; set; }
    public double OverallCoverage { get; set; }
    public Dictionary<string, double> AssemblyCoverages { get; set; } = new();
}

internal class TrendChartData
{
    public List<string> Labels { get; set; } = new();
    public List<double> CoverageValues { get; set; } = new();
}

internal enum CoverageTrendDirection
{
    Unknown,
    Improving,
    Stable,
    Declining
}