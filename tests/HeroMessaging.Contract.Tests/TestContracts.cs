namespace HeroMessaging.Contract.Tests;

// Shared test contract types used across test files
// These represent the contracts that need to be implemented

public enum TestCategory
{
    Unit,
    Integration,
    Contract,
    Performance
}

public class TestRunResult
{
    public Guid RunId { get; set; }
    public bool Success { get; set; }
    public TimeSpan Duration { get; set; }
    public TestResult[] Results { get; set; } = Array.Empty<TestResult>();
    public string? ErrorMessage { get; set; }
}

public class TestResult
{
    public string TestName { get; set; } = string.Empty;
    public TestCategory Category { get; set; }
    public bool Passed { get; set; }
    public TimeSpan ExecutionTime { get; set; }
    public string? FailureMessage { get; set; }
}

public class TestInfo
{
    public string Name { get; set; } = string.Empty;
    public TestCategory Category { get; set; }
    public TimeSpan EstimatedDuration { get; set; }
    public string Framework { get; set; } = string.Empty;
}