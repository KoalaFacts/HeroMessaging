using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Abstractions.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Configuration;

/// <summary>
/// Validates HeroMessaging configuration and plugin dependencies
/// </summary>
/// <remarks>
/// The configuration validator performs comprehensive validation of the HeroMessaging
/// service configuration including:
/// <list type="bullet">
/// <item><description>Required service registrations (IHeroMessaging, storage, serialization)</description></item>
/// <item><description>Storage configuration for outbox, inbox, and queue patterns</description></item>
/// <item><description>Serialization configuration for message persistence</description></item>
/// <item><description>Plugin dependencies and compatibility</description></item>
/// <item><description>Configuration consistency and duplicate registrations</description></item>
/// </list>
/// <example>
/// Example usage:
/// <code>
/// var services = new ServiceCollection();
/// services.AddHeroMessaging(options => {
///     // Configure HeroMessaging
/// });
///
/// var validator = new ConfigurationValidator(services, logger);
/// var report = validator.Validate();
///
/// if (!report.IsValid)
/// {
///     throw new InvalidOperationException(
///         $"Configuration validation failed: {report}"
///     );
/// }
/// </code>
/// </example>
/// </remarks>
public class ConfigurationValidator : IConfigurationValidator
{
    private readonly IServiceCollection _services;
    private readonly ILogger<ConfigurationValidator>? _logger;
    private readonly List<ValidationResult> _results = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationValidator"/> class
    /// </summary>
    /// <param name="services">The service collection to validate</param>
    /// <param name="logger">Optional logger for validation diagnostics</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null</exception>
    public ConfigurationValidator(IServiceCollection services, ILogger<ConfigurationValidator>? logger = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _logger = logger;
    }

    /// <summary>
    /// Validates the entire HeroMessaging configuration and returns a comprehensive report
    /// </summary>
    /// <returns>
    /// A <see cref="ValidationReport"/> containing all validation results including errors,
    /// warnings, and informational messages
    /// </returns>
    /// <remarks>
    /// This method performs the following validation checks:
    /// <list type="number">
    /// <item><description>Validates that required services are registered</description></item>
    /// <item><description>Validates storage configuration for enabled patterns</description></item>
    /// <item><description>Validates serialization configuration</description></item>
    /// <item><description>Validates plugin dependencies (at runtime)</description></item>
    /// <item><description>Validates absence of circular dependencies (at runtime)</description></item>
    /// <item><description>Validates configuration consistency and duplicate registrations</description></item>
    /// </list>
    /// The validation results are logged using the configured logger if available.
    /// </remarks>
    public IValidationReport Validate()
    {
        _results.Clear();

        // Check for required services
        ValidateRequiredServices();

        // Check for storage configuration
        ValidateStorageConfiguration();

        // Check for serialization configuration
        ValidateSerializationConfiguration();

        // Check for plugin dependencies
        ValidatePluginDependencies();

        // Check for circular dependencies
        ValidateCircularDependencies();

        // Check for configuration consistency
        ValidateConfigurationConsistency();

        return new ValidationReport(_results);
    }

    private void ValidateRequiredServices()
    {
        // Check if IHeroMessaging is registered
        if (!HasService<Abstractions.IHeroMessaging>())
        {
            AddError("IHeroMessaging is not registered. Ensure AddHeroMessaging() was called.");
        }
    }

    private void ValidateStorageConfiguration()
    {
        // Check if any storage is configured when outbox/inbox patterns are enabled
        var hasOutboxProcessor = HasService("HeroMessaging.Processing.OutboxProcessor");
        var hasInboxProcessor = HasService("HeroMessaging.Processing.InboxProcessor");

        if (hasOutboxProcessor && !HasService<IOutboxStorage>())
        {
            AddError("Outbox pattern is enabled but IOutboxStorage is not configured. Use ConfigureStorage() to set up storage.");
        }

        if (hasInboxProcessor && !HasService<IInboxStorage>())
        {
            AddError("Inbox pattern is enabled but IInboxStorage is not configured. Use ConfigureStorage() to set up storage.");
        }

        // Check queue storage if queues are enabled
        var hasQueueProcessor = HasService("HeroMessaging.Processing.QueueProcessor");
        if (hasQueueProcessor && !HasService<IQueueStorage>())
        {
            AddWarning("Queue pattern is enabled but IQueueStorage is not configured. Will use in-memory storage by default.");
        }
    }

    private void ValidateSerializationConfiguration()
    {
        // Check if serialization is configured when needed
        var hasOutboxProcessor = HasService("HeroMessaging.Processing.OutboxProcessor");
        var hasInboxProcessor = HasService("HeroMessaging.Processing.InboxProcessor");
        var hasQueueProcessor = HasService("HeroMessaging.Processing.QueueProcessor");

        if ((hasOutboxProcessor || hasInboxProcessor || hasQueueProcessor) && !HasService<IMessageSerializer>())
        {
            AddWarning("Message persistence patterns are enabled but IMessageSerializer is not configured. Consider configuring serialization for better performance.");
        }
    }

    private void ValidatePluginDependencies()
    {
        // Plugin dependency validation would be done at runtime
        // when the service provider is available
        _logger?.LogDebug("Plugin dependency validation will be performed at runtime");
    }

    private void ValidateCircularDependencies()
    {
        // Circular dependency validation would be done at runtime
        // when the service provider is available
        _logger?.LogDebug("Circular dependency validation will be performed at runtime");
    }

    private void ValidateConfigurationConsistency()
    {
        // Check for multiple registrations of the same service
        var serviceTypes = _services
            .Where(s => s.ServiceType.Namespace?.StartsWith("HeroMessaging") == true)
            .GroupBy(s => s.ServiceType)
            .Where(g => g.Count() > 1);

        foreach (var group in serviceTypes)
        {
            if (group.Key.IsInterface)
            {
                AddWarning($"Multiple implementations registered for {group.Key.Name}. Last registration will be used.");
            }
        }
    }

    private bool HasService<T>() where T : class
    {
        return _services.Any(s => s.ServiceType == typeof(T));
    }

    private bool HasService(string typeName)
    {
        return _services.Any(s => s.ServiceType.FullName == typeName);
    }

    private void AddError(string message)
    {
        _results.Add(new ValidationResult(ValidationSeverity.Error, message));
        _logger?.LogError(message);
    }

    private void AddWarning(string message)
    {
        _results.Add(new ValidationResult(ValidationSeverity.Warning, message));
        _logger?.LogWarning(message);
    }

    private void AddInfo(string message)
    {
        _results.Add(new ValidationResult(ValidationSeverity.Info, message));
        _logger?.LogInformation(message);
    }
}

/// <summary>
/// Represents a single validation result with severity level and message
/// </summary>
/// <remarks>
/// Validation results are created during configuration validation to indicate
/// errors, warnings, or informational messages about the HeroMessaging configuration.
/// <example>
/// Example usage:
/// <code>
/// var result = new ValidationResult(
///     ValidationSeverity.Error,
///     "IHeroMessaging is not registered. Ensure AddHeroMessaging() was called."
/// );
/// Console.WriteLine($"{result.Severity}: {result.Message}");
/// </code>
/// </example>
/// </remarks>
public class ValidationResult
{
    /// <summary>
    /// Gets the severity level of this validation result
    /// </summary>
    public ValidationSeverity Severity { get; }

    /// <summary>
    /// Gets the validation message describing the issue or information
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationResult"/> class
    /// </summary>
    /// <param name="severity">The severity level of the validation result</param>
    /// <param name="message">The validation message describing the issue or information</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="message"/> is null</exception>
    public ValidationResult(ValidationSeverity severity, string message)
    {
        Severity = severity;
        Message = message ?? throw new ArgumentNullException(nameof(message));
    }
}

/// <summary>
/// Defines the severity levels for configuration validation results
/// </summary>
/// <remarks>
/// Severity levels indicate the importance and impact of validation findings:
/// <list type="bullet">
/// <item><description><see cref="Info"/>: Informational messages that don't affect functionality</description></item>
/// <item><description><see cref="Warning"/>: Non-critical issues that should be reviewed but don't prevent operation</description></item>
/// <item><description><see cref="Error"/>: Critical issues that prevent proper configuration and must be resolved</description></item>
/// </list>
/// </remarks>
public enum ValidationSeverity
{
    /// <summary>
    /// Informational message providing additional context about the configuration
    /// </summary>
    /// <remarks>
    /// Info-level results do not indicate problems and are purely informational
    /// </remarks>
    Info,

    /// <summary>
    /// Warning message indicating a potential issue or non-optimal configuration
    /// </summary>
    /// <remarks>
    /// Warnings should be reviewed but do not prevent the system from operating.
    /// They typically indicate suboptimal configurations or deprecated patterns.
    /// </remarks>
    Warning,

    /// <summary>
    /// Error message indicating a critical configuration problem that must be resolved
    /// </summary>
    /// <remarks>
    /// Errors indicate that the configuration is invalid and the system may not
    /// function correctly. All errors must be resolved before deploying to production.
    /// </remarks>
    Error
}

/// <summary>
/// Represents a comprehensive validation report containing all configuration validation results
/// </summary>
/// <remarks>
/// The validation report aggregates all validation results and provides convenient access
/// to errors, warnings, and informational messages. It implements <see cref="IValidationReport"/>
/// and provides additional filtering and formatting capabilities.
/// <example>
/// Example usage:
/// <code>
/// var validator = new ConfigurationValidator(services);
/// var report = validator.Validate();
///
/// if (!report.IsValid)
/// {
///     foreach (var error in report.Errors)
///     {
///         Console.WriteLine($"ERROR: {error.Message}");
///     }
/// }
///
/// if (report.HasWarnings)
/// {
///     foreach (var warning in report.Warnings)
///     {
///         Console.WriteLine($"WARNING: {warning.Message}");
///     }
/// }
/// </code>
/// </example>
/// </remarks>
public class ValidationReport : IValidationReport
{
    private readonly List<ValidationResult> _results;

    /// <summary>
    /// Gets all validation results as a read-only list
    /// </summary>
    public IReadOnlyList<ValidationResult> Results => _results;

    /// <summary>
    /// Gets a value indicating whether the configuration is valid (contains no errors)
    /// </summary>
    /// <remarks>
    /// A configuration is considered valid if there are no error-level validation results.
    /// Warnings and informational messages do not affect validity.
    /// </remarks>
    public bool IsValid => !_results.Any(r => r.Severity == ValidationSeverity.Error);

    /// <summary>
    /// Gets a value indicating whether the report contains any warning-level results
    /// </summary>
    public bool HasWarnings => _results.Any(r => r.Severity == ValidationSeverity.Warning);

    /// <summary>
    /// Gets all error-level validation results
    /// </summary>
    /// <remarks>
    /// Errors indicate critical configuration problems that must be resolved
    /// before the system can operate correctly.
    /// </remarks>
    public IEnumerable<ValidationResult> Errors => _results.Where(r => r.Severity == ValidationSeverity.Error);

    /// <summary>
    /// Gets all warning-level validation results
    /// </summary>
    /// <remarks>
    /// Warnings indicate potential issues or non-optimal configurations that should
    /// be reviewed but do not prevent system operation.
    /// </remarks>
    public IEnumerable<ValidationResult> Warnings => _results.Where(r => r.Severity == ValidationSeverity.Warning);

    /// <summary>
    /// Gets all informational-level validation results
    /// </summary>
    /// <remarks>
    /// Informational results provide additional context about the configuration
    /// but do not indicate any problems.
    /// </remarks>
    public IEnumerable<ValidationResult> Information => _results.Where(r => r.Severity == ValidationSeverity.Info);

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationReport"/> class
    /// </summary>
    /// <param name="results">The collection of validation results to include in the report</param>
    /// <remarks>
    /// If <paramref name="results"/> is null, an empty list will be created.
    /// </remarks>
    public ValidationReport(IEnumerable<ValidationResult> results)
    {
        _results = results?.ToList() ?? new List<ValidationResult>();
    }

    /// <summary>
    /// Returns a string representation of the validation report
    /// </summary>
    /// <returns>
    /// A formatted string indicating the validation status and count of errors and warnings
    /// </returns>
    /// <remarks>
    /// If the configuration is valid with no warnings, returns "Configuration is valid".
    /// Otherwise, returns a summary of error and warning counts.
    /// </remarks>
    public override string ToString()
    {
        if (IsValid && !HasWarnings)
            return "Configuration is valid";

        var errors = Errors.Count();
        var warnings = Warnings.Count();

        return $"Configuration validation: {errors} error(s), {warnings} warning(s)";
    }
}