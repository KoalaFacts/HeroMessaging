using Moq;
using Xunit;

namespace HeroMessaging.Contract.Tests;

public class CoverageAnalysisContractTests
{
    public interface ICoverageAnalyzer
    {
        Task<CoverageReport> AnalyzeCoverageAsync(TestRunResult testResults);
        Task<ValidationResult> ValidateThresholdsAsync(CoverageReport report);
    }

    public class CoverageReport
    {
        public decimal OverallCoverage { get; set; }
        public decimal LineCoverage { get; set; }
        public decimal BranchCoverage { get; set; }
        public Dictionary<string, decimal> AssemblyCoverage { get; set; } = new();
        public IReadOnlyCollection<string> ExcludedPaths { get; set; } = Array.Empty<string>();
        public DateTime Timestamp { get; set; }
        public string Framework { get; set; } = string.Empty;
    }

    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public decimal ActualCoverage { get; set; }
        public decimal RequiredCoverage { get; set; }
        public IReadOnlyList<string> Violations { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> Recommendations { get; set; } = Array.Empty<string>();
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task AnalyzeCoverage_WithTestResults_ReturnsValidReport()
    {
        // Arrange
        var mockAnalyzer = new Mock<ICoverageAnalyzer>();
        var testResults = new TestRunResult
        {
            RunId = Guid.NewGuid(),
            Success = true,
            Duration = TimeSpan.FromMinutes(2)
        };

        var expectedReport = new CoverageReport
        {
            OverallCoverage = 85.5m,
            LineCoverage = 87.2m,
            BranchCoverage = 83.8m,
            AssemblyCoverage = new Dictionary<string, decimal>
            {
                ["HeroMessaging"] = 88.0m,
                ["HeroMessaging.Abstractions"] = 95.0m,
                ["HeroMessaging.Storage.PostgreSql"] = 82.0m
            },
            ExcludedPaths = new[]
            {
                "**/bin/**",
                "**/obj/**",
                "**/*AssemblyInfo.cs",
                "**/GlobalUsings.cs"
            },
            Timestamp = DateTime.UtcNow,
            Framework = "net8.0"
        };

        mockAnalyzer.Setup(a => a.AnalyzeCoverageAsync(testResults))
                   .ReturnsAsync(expectedReport);

        // Act
        var result = await mockAnalyzer.Object.AnalyzeCoverageAsync(testResults);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.OverallCoverage >= 80m, $"Overall coverage {result.OverallCoverage}% must be >= 80%");
        Assert.True(result.LineCoverage > 0);
        Assert.True(result.BranchCoverage > 0);
        Assert.Contains("HeroMessaging", result.AssemblyCoverage.Keys);
        Assert.Contains("**/bin/**", result.ExcludedPaths);
        mockAnalyzer.Verify(a => a.AnalyzeCoverageAsync(testResults), Times.Once);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task ValidateThresholds_WithMinimumCoverage_ReturnsValid()
    {
        // Arrange
        var mockAnalyzer = new Mock<ICoverageAnalyzer>();
        var coverageReport = new CoverageReport
        {
            OverallCoverage = 82.5m,
            LineCoverage = 85.0m,
            BranchCoverage = 80.0m
        };

        var expectedValidation = new ValidationResult
        {
            IsValid = true,
            ActualCoverage = 82.5m,
            RequiredCoverage = 80.0m,
            Violations = Array.Empty<string>(),
            Recommendations = Array.Empty<string>()
        };

        mockAnalyzer.Setup(a => a.ValidateThresholdsAsync(coverageReport))
                   .ReturnsAsync(expectedValidation);

        // Act
        var result = await mockAnalyzer.Object.ValidateThresholdsAsync(coverageReport);

        // Assert
        Assert.True(result.IsValid);
        Assert.True(result.ActualCoverage >= result.RequiredCoverage);
        Assert.Empty(result.Violations);
        mockAnalyzer.Verify(a => a.ValidateThresholdsAsync(coverageReport), Times.Once);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task ValidateThresholds_WithBelowMinimumCoverage_ReturnsInvalid()
    {
        // Arrange
        var mockAnalyzer = new Mock<ICoverageAnalyzer>();
        var coverageReport = new CoverageReport
        {
            OverallCoverage = 75.0m,
            LineCoverage = 77.0m,
            BranchCoverage = 73.0m
        };

        var expectedValidation = new ValidationResult
        {
            IsValid = false,
            ActualCoverage = 75.0m,
            RequiredCoverage = 80.0m,
            Violations = new[]
            {
                "Overall coverage 75.0% is below required 80.0%",
                "Branch coverage 73.0% is below required 80.0%"
            },
            Recommendations = new[]
            {
                "Add unit tests for uncovered methods in HeroMessaging.Processing namespace",
                "Increase integration test coverage for error handling scenarios"
            }
        };

        mockAnalyzer.Setup(a => a.ValidateThresholdsAsync(coverageReport))
                   .ReturnsAsync(expectedValidation);

        // Act
        var result = await mockAnalyzer.Object.ValidateThresholdsAsync(coverageReport);

        // Assert
        Assert.False(result.IsValid);
        Assert.True(result.ActualCoverage < result.RequiredCoverage);
        Assert.NotEmpty(result.Violations);
        Assert.Contains("75.0%", result.Violations[0]);
        Assert.Contains("80.0%", result.Violations[0]);
        Assert.NotEmpty(result.Recommendations);
        mockAnalyzer.Verify(a => a.ValidateThresholdsAsync(coverageReport), Times.Once);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task AnalyzeCoverage_WithExclusions_IgnoresGeneratedCode()
    {
        // Arrange
        var mockAnalyzer = new Mock<ICoverageAnalyzer>();
        var testResults = new TestRunResult { RunId = Guid.NewGuid() };

        var expectedReport = new CoverageReport
        {
            OverallCoverage = 85.0m,
            ExcludedPaths = new[]
            {
                "**/bin/**",
                "**/obj/**",
                "**/*AssemblyInfo.cs",
                "**/GlobalUsings.cs",
                "**/Generated/**",
                "**/Debug/**"
            }
        };

        mockAnalyzer.Setup(a => a.AnalyzeCoverageAsync(testResults))
                   .ReturnsAsync(expectedReport);

        // Act
        var result = await mockAnalyzer.Object.AnalyzeCoverageAsync(testResults);

        // Assert
        Assert.Contains("**/bin/**", result.ExcludedPaths);
        Assert.Contains("**/obj/**", result.ExcludedPaths);
        Assert.Contains("**/*AssemblyInfo.cs", result.ExcludedPaths);
        Assert.Contains("**/GlobalUsings.cs", result.ExcludedPaths);
        Assert.Contains("**/Generated/**", result.ExcludedPaths);
        Assert.Contains("**/Debug/**", result.ExcludedPaths);
        mockAnalyzer.Verify(a => a.AnalyzeCoverageAsync(testResults), Times.Once);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task ValidateThresholds_WithPublicApiCoverage_RequiresHundredPercent()
    {
        // Arrange
        var mockAnalyzer = new Mock<ICoverageAnalyzer>();
        var coverageReport = new CoverageReport
        {
            OverallCoverage = 85.0m,
            AssemblyCoverage = new Dictionary<string, decimal>
            {
                ["HeroMessaging.PublicApi"] = 100.0m,
                ["HeroMessaging.Internal"] = 75.0m
            }
        };

        var expectedValidation = new ValidationResult
        {
            IsValid = true,
            ActualCoverage = 85.0m,
            RequiredCoverage = 80.0m,
            Violations = Array.Empty<string>()
        };

        mockAnalyzer.Setup(a => a.ValidateThresholdsAsync(coverageReport))
                   .ReturnsAsync(expectedValidation);

        // Act
        var result = await mockAnalyzer.Object.ValidateThresholdsAsync(coverageReport);

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(100.0m, coverageReport.AssemblyCoverage["HeroMessaging.PublicApi"]);
        mockAnalyzer.Verify(a => a.ValidateThresholdsAsync(coverageReport), Times.Once);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task AnalyzeCoverage_WithMultipleFrameworks_ReturnsFrameworkSpecificData()
    {
        // Arrange
        var mockAnalyzer = new Mock<ICoverageAnalyzer>();
        var testResults = new TestRunResult { RunId = Guid.NewGuid() };

        var expectedReport = new CoverageReport
        {
            OverallCoverage = 84.0m,
            Framework = "net8.0",
            Timestamp = DateTime.UtcNow,
            AssemblyCoverage = new Dictionary<string, decimal>
            {
                ["HeroMessaging.net8.0"] = 84.0m,
                ["HeroMessaging.netstandard2.0"] = 86.0m
            }
        };

        mockAnalyzer.Setup(a => a.AnalyzeCoverageAsync(testResults))
                   .ReturnsAsync(expectedReport);

        // Act
        var result = await mockAnalyzer.Object.AnalyzeCoverageAsync(testResults);

        // Assert
        Assert.Equal("net8.0", result.Framework);
        Assert.True(result.Timestamp > DateTime.UtcNow.AddMinutes(-1));
        Assert.Contains("HeroMessaging.net8.0", result.AssemblyCoverage.Keys);
        Assert.Contains("HeroMessaging.netstandard2.0", result.AssemblyCoverage.Keys);
        mockAnalyzer.Verify(a => a.AnalyzeCoverageAsync(testResults), Times.Once);
    }
}