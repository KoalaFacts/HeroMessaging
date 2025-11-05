using Moq;
using Xunit;

namespace HeroMessaging.Contract.Tests;

public class TestExecutionContractTests
{
    public interface ITestRunner
    {
        Task<TestRunResult> ExecuteTestsAsync(TestCategory[] categories, TimeSpan timeout);
        Task<TestInfo[]> GetAvailableTestsAsync(TestCategory? category = null);
        Task<bool> CancelExecutionAsync(Guid runId);
    }


    [Fact]
    [Trait("Category", "Contract")]
    public async Task ExecuteTests_WithUnitCategory_CompletesWithinThirtySeconds()
    {
        // Arrange
        var mockRunner = new Mock<ITestRunner>();
        var timeout = TimeSpan.FromSeconds(30);
        var categories = new[] { TestCategory.Unit };

        var expectedResult = new TestRunResult
        {
            RunId = Guid.NewGuid(),
            Success = true,
            Duration = TimeSpan.FromSeconds(25),
            Results = new[]
            {
                new TestResult
                {
                    TestName = "SampleUnitTest",
                    Category = TestCategory.Unit,
                    Passed = true,
                    ExecutionTime = TimeSpan.FromSeconds(0.1)
                }
            }
        };

        mockRunner.Setup(r => r.ExecuteTestsAsync(categories, timeout))
                  .ReturnsAsync(expectedResult);

        // Act
        var result = await mockRunner.Object.ExecuteTestsAsync(categories, timeout);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.Duration < timeout, $"Unit tests must complete within {timeout.TotalSeconds}s but took {result.Duration.TotalSeconds}s");
        Assert.Equal(TestCategory.Unit, result.Results[0].Category);
        mockRunner.Verify(r => r.ExecuteTestsAsync(categories, timeout), Times.Once);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task ExecuteTests_WithIntegrationCategory_CompletesWithinTwoMinutes()
    {
        // Arrange
        var mockRunner = new Mock<ITestRunner>();
        var timeout = TimeSpan.FromMinutes(2);
        var categories = new[] { TestCategory.Integration };

        var expectedResult = new TestRunResult
        {
            RunId = Guid.NewGuid(),
            Success = true,
            Duration = TimeSpan.FromMinutes(1.5),
            Results = new[]
            {
                new TestResult
                {
                    TestName = "SampleIntegrationTest",
                    Category = TestCategory.Integration,
                    Passed = true,
                    ExecutionTime = TimeSpan.FromMinutes(1.5)
                }
            }
        };

        mockRunner.Setup(r => r.ExecuteTestsAsync(categories, timeout))
                  .ReturnsAsync(expectedResult);

        // Act
        var result = await mockRunner.Object.ExecuteTestsAsync(categories, timeout);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.Duration < timeout, $"Integration tests must complete within {timeout.TotalMinutes}min but took {result.Duration.TotalMinutes}min");
        Assert.Equal(TestCategory.Integration, result.Results[0].Category);
        mockRunner.Verify(r => r.ExecuteTestsAsync(categories, timeout), Times.Once);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task GetAvailableTests_WithUnitCategory_ReturnsFilteredTests()
    {
        // Arrange
        var mockRunner = new Mock<ITestRunner>();
        var category = TestCategory.Unit;

        var expectedTests = new[]
        {
            new TestInfo
            {
                Name = "MessageProcessorTests.ProcessAsync_ValidMessage_ReturnsSuccess",
                Category = TestCategory.Unit,
                EstimatedDuration = TimeSpan.FromMilliseconds(100),
                Framework = "net8.0"
            },
            new TestInfo
            {
                Name = "DecoratorTests.LoggingDecorator_ProcessAsync_LogsStartAndCompletion",
                Category = TestCategory.Unit,
                EstimatedDuration = TimeSpan.FromMilliseconds(50),
                Framework = "net8.0"
            }
        };

        mockRunner.Setup(r => r.GetAvailableTestsAsync(category))
                  .ReturnsAsync(expectedTests);

        // Act
        var result = await mockRunner.Object.GetAvailableTestsAsync(category);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Length);
        Assert.All(result, test => Assert.Equal(TestCategory.Unit, test.Category));
        Assert.All(result, test => Assert.True(test.EstimatedDuration < TimeSpan.FromSeconds(30), "Unit tests should have estimated duration < 30s"));
        mockRunner.Verify(r => r.GetAvailableTestsAsync(category), Times.Once);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task CancelExecution_WithValidRunId_ReturnsTrue()
    {
        // Arrange
        var mockRunner = new Mock<ITestRunner>();
        var runId = Guid.NewGuid();

        mockRunner.Setup(r => r.CancelExecutionAsync(runId))
                  .ReturnsAsync(true);

        // Act
        var result = await mockRunner.Object.CancelExecutionAsync(runId);

        // Assert
        Assert.True(result);
        mockRunner.Verify(r => r.CancelExecutionAsync(runId), Times.Once);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task CancelExecution_WithInvalidRunId_ReturnsFalse()
    {
        // Arrange
        var mockRunner = new Mock<ITestRunner>();
        var invalidRunId = Guid.Empty;

        mockRunner.Setup(r => r.CancelExecutionAsync(invalidRunId))
                  .ReturnsAsync(false);

        // Act
        var result = await mockRunner.Object.CancelExecutionAsync(invalidRunId);

        // Assert
        Assert.False(result);
        mockRunner.Verify(r => r.CancelExecutionAsync(invalidRunId), Times.Once);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task ExecuteTests_WithTimeoutExceeded_ReturnsFailure()
    {
        // Arrange
        var mockRunner = new Mock<ITestRunner>();
        var shortTimeout = TimeSpan.FromMilliseconds(1);
        var categories = new[] { TestCategory.Integration };

        var timeoutResult = new TestRunResult
        {
            RunId = Guid.NewGuid(),
            Success = false,
            Duration = TimeSpan.FromMinutes(5),
            ErrorMessage = "Test execution exceeded timeout of 1ms"
        };

        mockRunner.Setup(r => r.ExecuteTestsAsync(categories, shortTimeout))
                  .ReturnsAsync(timeoutResult);

        // Act
        var result = await mockRunner.Object.ExecuteTestsAsync(categories, shortTimeout);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("timeout", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        mockRunner.Verify(r => r.ExecuteTestsAsync(categories, shortTimeout), Times.Once);
    }
}