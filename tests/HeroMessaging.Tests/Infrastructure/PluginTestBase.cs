using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace HeroMessaging.Tests.Infrastructure;

/// <summary>
/// Base class for plugin testing with contract validation, isolation, and lifecycle management
/// Provides comprehensive infrastructure for testing plugins in isolation with dependency injection
/// </summary>
/// <typeparam name="TPlugin">Plugin interface type</typeparam>
public abstract class PluginTestBase<TPlugin> : IAsyncDisposable where TPlugin : class
{
    protected readonly ITestOutputHelper Output;
    protected readonly PluginTestContext<TPlugin> Context;
    private readonly List<IAsyncDisposable> _disposables = new();
    private bool _disposed = false;

    protected PluginTestBase(ITestOutputHelper output)
    {
        Output = output;
        Context = new PluginTestContext<TPlugin>(output);
    }

    /// <summary>
    /// Creates and configures a plugin instance for testing
    /// </summary>
    /// <typeparam name="TImplementation">Concrete plugin implementation</typeparam>
    /// <param name="configuration">Plugin configuration</param>
    /// <returns>Configured plugin instance</returns>
    protected async Task<TImplementation> CreatePluginAsync<TImplementation>(
        object? configuration = null)
        where TImplementation : class, TPlugin, new()
    {
        var plugin = new TImplementation();

        if (plugin is IPluginWithConfiguration configurable && configuration != null)
        {
            await configurable.ConfigureAsync(configuration);
        }

        if (plugin is IPluginWithLifecycle lifecycle)
        {
            await lifecycle.InitializeAsync();
            Context.RegisterForCleanup(lifecycle);
        }

        if (plugin is IAsyncDisposable disposable)
        {
            _disposables.Add(disposable);
        }

        return plugin;
    }

    /// <summary>
    /// Creates a plugin with mock dependencies
    /// </summary>
    /// <typeparam name="TImplementation">Concrete plugin implementation</typeparam>
    /// <param name="mockDependencies">Mock dependencies to inject</param>
    /// <returns>Plugin with mocked dependencies</returns>
    protected async Task<TImplementation> CreatePluginWithMocksAsync<TImplementation>(
        Dictionary<Type, object>? mockDependencies = null)
        where TImplementation : class, TPlugin
    {
        var mockContainer = new MockDependencyContainer(mockDependencies ?? new Dictionary<Type, object>());

        var plugin = mockContainer.CreateInstance<TImplementation>();

        if (plugin is IPluginWithLifecycle lifecycle)
        {
            await lifecycle.InitializeAsync();
            Context.RegisterForCleanup(lifecycle);
        }

        if (plugin is IAsyncDisposable disposable)
        {
            _disposables.Add(disposable);
        }

        return plugin;
    }

    /// <summary>
    /// Validates that a plugin implements required contracts
    /// </summary>
    /// <param name="plugin">Plugin to validate</param>
    /// <returns>Contract validation results</returns>
    protected PluginContractValidation ValidatePluginContract(TPlugin plugin)
    {
        var validation = new PluginContractValidation();
        var pluginType = plugin.GetType();

        try
        {
            // Check required interface implementation
            if (!typeof(TPlugin).IsAssignableFrom(pluginType))
            {
                validation.Errors.Add($"Plugin {pluginType.Name} does not implement {typeof(TPlugin).Name}");
            }

            // Check for required attributes
            var pluginAttribute = pluginType.GetCustomAttribute<PluginAttribute>();
            if (pluginAttribute == null)
            {
                validation.Warnings.Add($"Plugin {pluginType.Name} missing PluginAttribute");
            }
            else
            {
                validation.PluginName = pluginAttribute.Name;
                validation.PluginVersion = pluginAttribute.Version;
            }

            // Validate public API
            ValidatePublicApi(pluginType, validation);

            // Check lifecycle implementation
            if (plugin is IPluginWithLifecycle)
            {
                validation.HasLifecycleSupport = true;
            }

            // Check configuration support
            if (plugin is IPluginWithConfiguration)
            {
                validation.HasConfigurationSupport = true;
            }

            validation.IsValid = !validation.Errors.Any();
        }
        catch (Exception ex)
        {
            validation.Errors.Add($"Contract validation failed: {ex.Message}");
            validation.IsValid = false;
        }

        return validation;
    }

    /// <summary>
    /// Tests plugin isolation by ensuring it doesn't interfere with other instances
    /// </summary>
    /// <typeparam name="TImplementation">Plugin implementation type</typeparam>
    /// <returns>Isolation test results</returns>
    protected async Task<PluginIsolationTestResults> TestPluginIsolationAsync<TImplementation>()
        where TImplementation : class, TPlugin, new()
    {
        var results = new PluginIsolationTestResults();

        try
        {
            // Create multiple instances
            var instance1 = await CreatePluginAsync<TImplementation>();
            var instance2 = await CreatePluginAsync<TImplementation>();

            // Test that instances are separate
            results.InstancesAreSeparate = !ReferenceEquals(instance1, instance2);

            // Test state isolation if plugin has state
            if (instance1 is IPluginWithState stateful1 && instance2 is IPluginWithState stateful2)
            {
                // Modify state in instance1
                await stateful1.SetStateAsync("test-key", "test-value-1");
                await stateful2.SetStateAsync("test-key", "test-value-2");

                // Verify isolation
                var value1 = await stateful1.GetStateAsync("test-key");
                var value2 = await stateful2.GetStateAsync("test-key");

                results.StateIsIsolated = value1?.ToString() == "test-value-1" &&
                                        value2?.ToString() == "test-value-2";
            }
            else
            {
                results.StateIsIsolated = true; // No state to isolate
            }

            results.Success = results.InstancesAreSeparate && results.StateIsIsolated;
        }
        catch (Exception ex)
        {
            results.Success = false;
            results.ErrorMessage = ex.Message;
        }

        return results;
    }

    /// <summary>
    /// Tests plugin performance against specified benchmarks
    /// </summary>
    /// <param name="plugin">Plugin to test</param>
    /// <param name="benchmarks">Performance benchmarks to validate</param>
    /// <returns>Performance test results</returns>
    protected async Task<PluginPerformanceResults> TestPluginPerformanceAsync(
        TPlugin plugin,
        PerformanceBenchmarks benchmarks)
    {
        var results = new PluginPerformanceResults();

        try
        {
            if (plugin is IPluginWithPerformanceTests performanceTestable)
            {
                // Test latency
                var latencyResults = await MeasureLatencyAsync(performanceTestable, benchmarks.MaxLatencyMs);
                results.LatencyResults = latencyResults;

                // Test throughput
                var throughputResults = await MeasureThroughputAsync(performanceTestable, benchmarks.MinThroughputOpsPerSecond);
                results.ThroughputResults = throughputResults;

                // Test memory usage
                var memoryResults = await MeasureMemoryUsageAsync(performanceTestable, benchmarks.MaxMemoryMB);
                results.MemoryResults = memoryResults;

                results.Success = latencyResults.MeetsRequirement &&
                                throughputResults.MeetsRequirement &&
                                memoryResults.MeetsRequirement;
            }
            else
            {
                results.Success = true;
                results.Message = "Plugin does not implement performance testing interface";
            }
        }
        catch (Exception ex)
        {
            results.Success = false;
            results.ErrorMessage = ex.Message;
        }

        return results;
    }

    /// <summary>
    /// Tests plugin configuration validation and handling
    /// </summary>
    /// <param name="plugin">Plugin to test</param>
    /// <param name="testConfigurations">Test configurations to validate</param>
    /// <returns>Configuration test results</returns>
    protected async Task<PluginConfigurationTestResults> TestPluginConfigurationAsync(
        TPlugin plugin,
        IEnumerable<object> testConfigurations)
    {
        var results = new PluginConfigurationTestResults();

        try
        {
            if (plugin is IPluginWithConfiguration configurable)
            {
                foreach (var config in testConfigurations)
                {
                    var configResult = new ConfigurationTestResult
                    {
                        Configuration = config,
                        ConfigurationType = config.GetType().Name
                    };

                    try
                    {
                        await configurable.ConfigureAsync(config);
                        configResult.Success = true;
                    }
                    catch (Exception ex)
                    {
                        configResult.Success = false;
                        configResult.ErrorMessage = ex.Message;
                    }

                    results.ConfigurationResults.Add(configResult);
                }

                results.Success = results.ConfigurationResults.All(r => r.Success);
            }
            else
            {
                results.Success = true;
                results.Message = "Plugin does not support configuration";
            }
        }
        catch (Exception ex)
        {
            results.Success = false;
            results.ErrorMessage = ex.Message;
        }

        return results;
    }

    /// <summary>
    /// Creates a test scenario with predefined conditions
    /// </summary>
    /// <param name="scenarioName">Name of the test scenario</param>
    /// <param name="setupAction">Action to set up the scenario</param>
    /// <returns>Test scenario context</returns>
    protected PluginTestScenario CreateTestScenario(string scenarioName, Func<Task>? setupAction = null)
    {
        return new PluginTestScenario(scenarioName, setupAction, Output);
    }

    private void ValidatePublicApi(Type pluginType, PluginContractValidation validation)
    {
        var publicMethods = pluginType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.DeclaringType == pluginType)
            .ToList();

        foreach (var method in publicMethods)
        {
            // Check for proper async patterns
            if (method.Name.EndsWith("Async") && method.ReturnType != typeof(Task) && !method.ReturnType.IsGenericType)
            {
                validation.Warnings.Add($"Method {method.Name} should return Task or Task<T>");
            }

            // Check for cancellation token support on async methods
            if (method.Name.EndsWith("Async"))
            {
                var parameters = method.GetParameters();
                var hasCancellationToken = parameters.Any(p => p.ParameterType == typeof(CancellationToken));

                if (!hasCancellationToken)
                {
                    validation.Warnings.Add($"Async method {method.Name} should accept CancellationToken");
                }
            }
        }
    }

    private async Task<LatencyTestResult> MeasureLatencyAsync(IPluginWithPerformanceTests plugin, double maxLatencyMs)
    {
        var iterations = 1000;
        var latencies = new List<double>();

        for (int i = 0; i < iterations; i++)
        {
            var start = DateTime.UtcNow;
            await plugin.ExecutePerformanceTestAsync();
            var end = DateTime.UtcNow;

            latencies.Add((end - start).TotalMilliseconds);
        }

        var averageLatency = latencies.Average();
        var p99Latency = latencies.OrderBy(x => x).Skip((int)(iterations * 0.99)).First();

        return new LatencyTestResult
        {
            AverageLatencyMs = averageLatency,
            P99LatencyMs = p99Latency,
            MaxAllowedLatencyMs = maxLatencyMs,
            MeetsRequirement = p99Latency <= maxLatencyMs
        };
    }

    private async Task<ThroughputTestResult> MeasureThroughputAsync(IPluginWithPerformanceTests plugin, double minThroughputOpsPerSecond)
    {
        var testDurationSeconds = 5;
        var operations = 0;

        var start = DateTime.UtcNow;
        var end = start.AddSeconds(testDurationSeconds);

        while (DateTime.UtcNow < end)
        {
            await plugin.ExecutePerformanceTestAsync();
            operations++;
        }

        var actualDuration = (DateTime.UtcNow - start).TotalSeconds;
        var throughput = operations / actualDuration;

        return new ThroughputTestResult
        {
            ActualThroughputOpsPerSecond = throughput,
            MinRequiredThroughputOpsPerSecond = minThroughputOpsPerSecond,
            MeetsRequirement = throughput >= minThroughputOpsPerSecond
        };
    }

    private async Task<MemoryTestResult> MeasureMemoryUsageAsync(IPluginWithPerformanceTests plugin, double maxMemoryMB)
    {
        var startMemory = GC.GetTotalMemory(true);

        // Execute operations to measure memory impact
        for (int i = 0; i < 1000; i++)
        {
            await plugin.ExecutePerformanceTestAsync();
        }

        var endMemory = GC.GetTotalMemory(false);
        var memoryUsedBytes = endMemory - startMemory;
        var memoryUsedMB = memoryUsedBytes / (1024.0 * 1024.0);

        return new MemoryTestResult
        {
            MemoryUsedMB = memoryUsedMB,
            MaxAllowedMemoryMB = maxMemoryMB,
            MeetsRequirement = memoryUsedMB <= maxMemoryMB
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            await Context.DisposeAsync();

            foreach (var disposable in _disposables)
            {
                await disposable.DisposeAsync();
            }

            _disposed = true;
        }
    }
}

/// <summary>
/// Test context for plugin testing with cleanup management
/// </summary>
/// <typeparam name="TPlugin">Plugin type</typeparam>
public class PluginTestContext<TPlugin> : IAsyncDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<IPluginWithLifecycle> _lifecyclePlugins = new();
    private bool _disposed = false;

    public PluginTestContext(ITestOutputHelper output)
    {
        _output = output;
    }

    public void RegisterForCleanup(IPluginWithLifecycle plugin)
    {
        _lifecyclePlugins.Add(plugin);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            foreach (var plugin in _lifecyclePlugins)
            {
                try
                {
                    await plugin.DisposeAsync();
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"Warning: Error disposing plugin: {ex.Message}");
                }
            }

            _disposed = true;
        }
    }
}

/// <summary>
/// Mock dependency container for plugin testing
/// </summary>
public class MockDependencyContainer
{
    private readonly Dictionary<Type, object> _dependencies;

    public MockDependencyContainer(Dictionary<Type, object> dependencies)
    {
        _dependencies = dependencies;
    }

    public T CreateInstance<T>() where T : class
    {
        var type = typeof(T);
        var constructors = type.GetConstructors();

        foreach (var constructor in constructors.OrderByDescending(c => c.GetParameters().Length))
        {
            var parameters = constructor.GetParameters();
            var args = new object[parameters.Length];
            bool canCreateInstance = true;

            for (int i = 0; i < parameters.Length; i++)
            {
                var paramType = parameters[i].ParameterType;
                if (_dependencies.TryGetValue(paramType, out var dependency))
                {
                    args[i] = dependency;
                }
                else
                {
                    canCreateInstance = false;
                    break;
                }
            }

            if (canCreateInstance)
            {
                return (T)Activator.CreateInstance(type, args)!;
            }
        }

        // Fall back to parameterless constructor
        return (T)Activator.CreateInstance(type)!;
    }
}

/// <summary>
/// Test scenario context for organized plugin testing
/// </summary>
public class PluginTestScenario
{
    public string Name { get; }
    public Func<Task>? SetupAction { get; }
    public ITestOutputHelper Output { get; }

    public PluginTestScenario(string name, Func<Task>? setupAction, ITestOutputHelper output)
    {
        Name = name;
        SetupAction = setupAction;
        Output = output;
    }

    public async Task ExecuteAsync(Func<Task> testAction)
    {
        Output.WriteLine($"Executing scenario: {Name}");

        try
        {
            if (SetupAction != null)
            {
                await SetupAction();
            }

            await testAction();
            Output.WriteLine($"Scenario '{Name}' completed successfully");
        }
        catch (Exception ex)
        {
            Output.WriteLine($"Scenario '{Name}' failed: {ex.Message}");
            throw;
        }
    }
}

// Supporting interfaces and data structures

public interface IPluginWithLifecycle : IAsyncDisposable
{
    Task InitializeAsync();
}

public interface IPluginWithConfiguration
{
    Task ConfigureAsync(object configuration);
}

public interface IPluginWithState
{
    Task SetStateAsync(string key, object value);
    Task<object?> GetStateAsync(string key);
}

public interface IPluginWithPerformanceTests
{
    Task ExecutePerformanceTestAsync();
}

[AttributeUsage(AttributeTargets.Class)]
public class PluginAttribute : Attribute
{
    public string Name { get; }
    public string Version { get; }

    public PluginAttribute(string name, string version)
    {
        Name = name;
        Version = version;
    }
}

public class PluginContractValidation
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public string? PluginName { get; set; }
    public string? PluginVersion { get; set; }
    public bool HasLifecycleSupport { get; set; }
    public bool HasConfigurationSupport { get; set; }
}

public class PluginIsolationTestResults
{
    public bool Success { get; set; }
    public bool InstancesAreSeparate { get; set; }
    public bool StateIsIsolated { get; set; }
    public string? ErrorMessage { get; set; }
}

public class PluginPerformanceResults
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Message { get; set; }
    public LatencyTestResult? LatencyResults { get; set; }
    public ThroughputTestResult? ThroughputResults { get; set; }
    public MemoryTestResult? MemoryResults { get; set; }
}

public class PluginConfigurationTestResults
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Message { get; set; }
    public List<ConfigurationTestResult> ConfigurationResults { get; set; } = new();
}

public class ConfigurationTestResult
{
    public object? Configuration { get; set; }
    public string ConfigurationType { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

public class PerformanceBenchmarks
{
    public double MaxLatencyMs { get; set; } = 1.0;
    public double MinThroughputOpsPerSecond { get; set; } = 100000;
    public double MaxMemoryMB { get; set; } = 10.0;
}

public class LatencyTestResult
{
    public double AverageLatencyMs { get; set; }
    public double P99LatencyMs { get; set; }
    public double MaxAllowedLatencyMs { get; set; }
    public bool MeetsRequirement { get; set; }
}

public class ThroughputTestResult
{
    public double ActualThroughputOpsPerSecond { get; set; }
    public double MinRequiredThroughputOpsPerSecond { get; set; }
    public bool MeetsRequirement { get; set; }
}

public class MemoryTestResult
{
    public double MemoryUsedMB { get; set; }
    public double MaxAllowedMemoryMB { get; set; }
    public bool MeetsRequirement { get; set; }
}