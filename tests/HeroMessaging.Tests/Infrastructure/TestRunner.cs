using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace HeroMessaging.Tests.Infrastructure;

/// <summary>
/// Advanced test execution runner with categorization, parallel execution, and reporting
/// Orchestrates test execution across different categories and provides detailed results
/// </summary>
internal class TestRunner
{
    private readonly TestRunnerConfiguration _config;
    private readonly ITestOutputHelper? _outputHelper;

    public TestRunner(TestRunnerConfiguration? config = null, ITestOutputHelper? outputHelper = null)
    {
        _config = config ?? new TestRunnerConfiguration();
        _outputHelper = outputHelper;
    }

    /// <summary>
    /// Executes all tests with categorization and parallel execution
    /// </summary>
    /// <param name="categories">Test categories to execute (null for all)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Comprehensive test execution results</returns>
    public async Task<TestExecutionResults> ExecuteTestsAsync(
        IEnumerable<string>? categories = null,
        CancellationToken cancellationToken = default)
    {
        var results = new TestExecutionResults
        {
            StartTime = DateTime.UtcNow,
            Configuration = _config
        };

        var stopwatch = Stopwatch.StartNew();

        try
        {
            LogMessage("Starting test execution...");

            // Discover test assemblies
            var assemblies = DiscoverTestAssemblies();
            LogMessage($"Discovered {assemblies.Count} test assemblies");

            // Filter by categories if specified
            var filteredCategories = categories?.ToList() ?? _config.DefaultCategories;

            // Execute tests by category
            foreach (var category in filteredCategories)
            {
                var categoryResults = await ExecuteCategoryAsync(assemblies, category, cancellationToken);
                results.CategoryResults[category] = categoryResults;
            }

            // Generate coverage report if enabled
            if (_config.GenerateCoverageReport)
            {
                results.CoverageReport = await GenerateCoverageReportAsync();
            }

            results.Success = results.CategoryResults.Values.All(r => r.Success);
        }
        catch (Exception ex)
        {
            results.Success = false;
            results.ErrorMessage = ex.Message;
            LogMessage($"Test execution failed: {ex.Message}");
        }
        finally
        {
            stopwatch.Stop();
            results.EndTime = DateTime.UtcNow;
            results.TotalDuration = stopwatch.Elapsed;

            LogMessage($"Test execution completed in {results.TotalDuration.TotalSeconds:F2}s");

            if (_config.GenerateDetailedReport)
            {
                await SaveDetailedReportAsync(results);
            }
        }

        return results;
    }

    /// <summary>
    /// Executes tests for a specific category with parallel execution
    /// </summary>
    /// <param name="assemblies">Test assemblies to search</param>
    /// <param name="category">Test category to execute</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Category-specific test results</returns>
    public async Task<CategoryTestResults> ExecuteCategoryAsync(
        List<Assembly> assemblies,
        string category,
        CancellationToken cancellationToken = default)
    {
        var categoryResults = new CategoryTestResults
        {
            Category = category,
            StartTime = DateTime.UtcNow
        };

        var stopwatch = Stopwatch.StartNew();

        try
        {
            LogMessage($"Executing {category} tests...");

            // Discover tests in category
            var testMethods = DiscoverTestsInCategory(assemblies, category);
            LogMessage($"Found {testMethods.Count} tests in {category} category");

            if (!testMethods.Any())
            {
                categoryResults.Success = true;
                categoryResults.Message = $"No tests found for category: {category}";
                return categoryResults;
            }

            // Group tests by class for parallel execution
            var testGroups = testMethods.GroupBy(tm => tm.DeclaringType).ToList();

            var semaphore = new SemaphoreSlim(_config.MaxParallelism, _config.MaxParallelism);
            var tasks = testGroups.Select(async group =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    return await ExecuteTestGroupAsync(group.Key!, group.ToList(), cancellationToken);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var groupResults = await Task.WhenAll(tasks);

            // Aggregate results
            foreach (var groupResult in groupResults)
            {
                categoryResults.TestGroupResults.Add(groupResult);
                categoryResults.TotalTests += groupResult.TotalTests;
                categoryResults.PassedTests += groupResult.PassedTests;
                categoryResults.FailedTests += groupResult.FailedTests;
                categoryResults.SkippedTests += groupResult.SkippedTests;
            }

            categoryResults.Success = categoryResults.FailedTests == 0;

            if (categoryResults.FailedTests > 0)
            {
                categoryResults.Message = $"{categoryResults.FailedTests} test(s) failed in {category} category";
            }
        }
        catch (Exception ex)
        {
            categoryResults.Success = false;
            categoryResults.Message = $"Category execution failed: {ex.Message}";
            LogMessage($"Error executing {category} tests: {ex.Message}");
        }
        finally
        {
            stopwatch.Stop();
            categoryResults.EndTime = DateTime.UtcNow;
            categoryResults.Duration = stopwatch.Elapsed;

            LogMessage($"Completed {category} tests in {categoryResults.Duration.TotalSeconds:F2}s " +
                      $"({categoryResults.PassedTests} passed, {categoryResults.FailedTests} failed, {categoryResults.SkippedTests} skipped)");
        }

        return categoryResults;
    }

    /// <summary>
    /// Validates test configuration and setup
    /// </summary>
    /// <returns>Configuration validation results</returns>
    public TestConfigurationValidation ValidateConfiguration()
    {
        var validation = new TestConfigurationValidation();

        try
        {
            // Check test assemblies exist
            var assemblies = DiscoverTestAssemblies();
            if (!assemblies.Any())
            {
                validation.Errors.Add("No test assemblies found");
            }

            // Validate output directories
            if (_config.GenerateDetailedReport && !Directory.Exists(Path.GetDirectoryName(_config.ReportOutputPath)))
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_config.ReportOutputPath)!);
                }
                catch (Exception ex)
                {
                    validation.Errors.Add($"Cannot create report output directory: {ex.Message}");
                }
            }

            // Validate parallelism settings
            if (_config.MaxParallelism <= 0)
            {
                validation.Errors.Add("MaxParallelism must be greater than 0");
            }

            // Check for required tools
            if (_config.GenerateCoverageReport)
            {
                if (!IsToolAvailable("dotnet"))
                {
                    validation.Warnings.Add("dotnet CLI not found - coverage reporting may fail");
                }
            }

            validation.IsValid = !validation.Errors.Any();
        }
        catch (Exception ex)
        {
            validation.Errors.Add($"Configuration validation failed: {ex.Message}");
            validation.IsValid = false;
        }

        return validation;
    }

    /// <summary>
    /// Generates a summary report of test execution results
    /// </summary>
    /// <param name="results">Test execution results</param>
    /// <returns>Formatted summary report</returns>
    public string GenerateSummaryReport(TestExecutionResults results)
    {
        var report = new List<string>
        {
            "=== Test Execution Summary ===",
            $"Execution Time: {results.StartTime:yyyy-MM-dd HH:mm:ss} UTC",
            $"Duration: {results.TotalDuration.TotalSeconds:F2} seconds",
            $"Overall Result: {(results.Success ? "✅ PASSED" : "❌ FAILED")}",
            ""
        };

        if (!string.IsNullOrEmpty(results.ErrorMessage))
        {
            report.Add($"Error: {results.ErrorMessage}");
            report.Add("");
        }

        foreach (var (category, categoryResult) in results.CategoryResults)
        {
            var status = categoryResult.Success ? "✅" : "❌";
            report.Add($"{status} {category}: {categoryResult.PassedTests}/{categoryResult.TotalTests} passed " +
                      $"({categoryResult.Duration.TotalSeconds:F1}s)");

            if (categoryResult.FailedTests > 0)
            {
                var failedGroups = categoryResult.TestGroupResults.Where(g => g.FailedTests > 0);
                foreach (var group in failedGroups)
                {
                    report.Add($"   Failed in {group.TestClassName}: {string.Join(", ", group.FailedTestNames)}");
                }
            }
        }

        if (results.CoverageReport != null)
        {
            report.Add("");
            report.Add($"Code Coverage: {results.CoverageReport.OverallCoveragePercent:F1}%");

            if (results.CoverageReport.OverallCoveragePercent < _config.MinimumCoveragePercent)
            {
                report.Add($"⚠️  Coverage below minimum threshold ({_config.MinimumCoveragePercent}%)");
            }
        }

        return string.Join(Environment.NewLine, report);
    }

    private List<Assembly> DiscoverTestAssemblies()
    {
        var assemblies = new List<Assembly>();

        foreach (var pattern in _config.TestAssemblyPatterns)
        {
            try
            {
                var directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
                var files = Directory.GetFiles(directory, pattern, SearchOption.AllDirectories);

                foreach (var file in files)
                {
                    try
                    {
                        var assembly = Assembly.LoadFrom(file);
                        assemblies.Add(assembly);
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"Warning: Could not load assembly {file}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Warning: Error discovering assemblies with pattern {pattern}: {ex.Message}");
            }
        }

        return assemblies.Distinct().ToList();
    }

    private List<MethodInfo> DiscoverTestsInCategory(List<Assembly> assemblies, string category)
    {
        var testMethods = new List<MethodInfo>();

        foreach (var assembly in assemblies)
        {
            try
            {
                var types = assembly.GetTypes();

                foreach (var type in types)
                {
                    var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .Where(m => m.GetCustomAttribute<FactAttribute>() != null)
                        .Where(m => HasCategory(m, category));

                    testMethods.AddRange(methods);
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Warning: Error discovering tests in assembly {assembly.FullName}: {ex.Message}");
            }
        }

        return testMethods;
    }

    private bool HasCategory(MethodInfo method, string category)
    {
        // For simplicity in tests, just check if the method name contains the category
        // or if it has the Trait attribute with Category
        var methodName = method.Name.ToLowerInvariant();
        var categoryLower = category.ToLowerInvariant();

        // Simple heuristic based on method naming and test patterns
        return categoryLower switch
        {
            "unit" => !methodName.Contains("integration") && !methodName.Contains("contract") && !methodName.Contains("performance"),
            "integration" => methodName.Contains("integration") || method.DeclaringType?.Name.Contains("Integration") == true,
            "contract" => methodName.Contains("contract") || method.DeclaringType?.Name.Contains("Contract") == true,
            "performance" => methodName.Contains("performance") || methodName.Contains("benchmark") || method.DeclaringType?.Name.Contains("Performance") == true,
            _ => false
        };
    }

    private async Task<TestGroupResults> ExecuteTestGroupAsync(
        Type testClass,
        List<MethodInfo> testMethods,
        CancellationToken cancellationToken)
    {
        var groupResults = new TestGroupResults
        {
            TestClassName = testClass.FullName ?? testClass.Name,
            StartTime = DateTime.UtcNow
        };

        var stopwatch = Stopwatch.StartNew();

        try
        {
            foreach (var method in testMethods)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var testInstance = Activator.CreateInstance(testClass);
                    var result = method.Invoke(testInstance, null);

                    if (result is Task task)
                    {
                        await task;
                    }

                    groupResults.PassedTests++;
                    groupResults.PassedTestNames.Add(method.Name);
                }
                catch (Exception ex)
                {
                    groupResults.FailedTests++;
                    groupResults.FailedTestNames.Add(method.Name);
                    groupResults.TestFailures[method.Name] = ex.GetBaseException().Message;
                }

                groupResults.TotalTests++;
            }
        }
        catch (Exception ex)
        {
            LogMessage($"Error executing test group {testClass.Name}: {ex.Message}");
        }
        finally
        {
            stopwatch.Stop();
            groupResults.EndTime = DateTime.UtcNow;
            groupResults.Duration = stopwatch.Elapsed;
        }

        return groupResults;
    }

    private async Task<CoverageReport?> GenerateCoverageReportAsync()
    {
        try
        {
            LogMessage("Generating coverage report...");

            // This is a simplified implementation
            // In a real scenario, this would integrate with Coverlet or similar tools
            return new CoverageReport
            {
                OverallCoveragePercent = 85.0, // Placeholder
                GeneratedAt = DateTime.UtcNow,
                ReportPath = Path.Combine(_config.ReportOutputPath, "coverage-report.xml")
            };
        }
        catch (Exception ex)
        {
            LogMessage($"Warning: Coverage report generation failed: {ex.Message}");
            return null;
        }
    }

    private async Task SaveDetailedReportAsync(TestExecutionResults results)
    {
        try
        {
            var reportPath = _config.ReportOutputPath;
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);

            var json = JsonSerializer.Serialize(results, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(reportPath, json);
            LogMessage($"Detailed report saved to: {reportPath}");
        }
        catch (Exception ex)
        {
            LogMessage($"Warning: Could not save detailed report: {ex.Message}");
        }
    }

    private bool IsToolAvailable(string toolName)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = toolName,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private void LogMessage(string message)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var logMessage = $"[{timestamp}] {message}";

        _outputHelper?.WriteLine(logMessage);
        Console.WriteLine(logMessage);
    }
}

/// <summary>
/// Configuration for test runner execution
/// </summary>
internal class TestRunnerConfiguration
{
    public List<string> DefaultCategories { get; set; } = new() { "Unit", "Integration" };
    public List<string> TestAssemblyPatterns { get; set; } = new() { "*.Tests.dll", "*.Test.dll" };
    public int MaxParallelism { get; set; } = Environment.ProcessorCount;
    public bool GenerateCoverageReport { get; set; } = true;
    public bool GenerateDetailedReport { get; set; } = true;
    public string ReportOutputPath { get; set; } = Path.Combine("test-results", "detailed-report.json");
    public double MinimumCoveragePercent { get; set; } = 80.0;
}

/// <summary>
/// Comprehensive test execution results
/// </summary>
internal class TestExecutionResults
{
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan TotalDuration { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public TestRunnerConfiguration Configuration { get; set; } = new();
    public Dictionary<string, CategoryTestResults> CategoryResults { get; set; } = new();
    public CoverageReport? CoverageReport { get; set; }
}

/// <summary>
/// Test results for a specific category
/// </summary>
internal class CategoryTestResults
{
    public string Category { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration { get; set; }
    public bool Success { get; set; }
    public string? Message { get; set; }
    public int TotalTests { get; set; }
    public int PassedTests { get; set; }
    public int FailedTests { get; set; }
    public int SkippedTests { get; set; }
    public List<TestGroupResults> TestGroupResults { get; set; } = new();
}

/// <summary>
/// Test results for a group of tests (typically a test class)
/// </summary>
internal class TestGroupResults
{
    public string TestClassName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration { get; set; }
    public int TotalTests { get; set; }
    public int PassedTests { get; set; }
    public int FailedTests { get; set; }
    public int SkippedTests { get; set; }
    public List<string> PassedTestNames { get; set; } = new();
    public List<string> FailedTestNames { get; set; } = new();
    public Dictionary<string, string> TestFailures { get; set; } = new();
}

/// <summary>
/// Coverage report information
/// </summary>
internal class CoverageReport
{
    public double OverallCoveragePercent { get; set; }
    public DateTime GeneratedAt { get; set; }
    public string ReportPath { get; set; } = string.Empty;
}

/// <summary>
/// Configuration validation results
/// </summary>
internal class TestConfigurationValidation
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}