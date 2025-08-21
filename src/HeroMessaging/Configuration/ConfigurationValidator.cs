using System;
using System.Collections.Generic;
using System.Linq;
using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Plugins;
using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Abstractions.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Configuration;

/// <summary>
/// Validates HeroMessaging configuration and plugin dependencies
/// </summary>
public class ConfigurationValidator : IConfigurationValidator
{
    private readonly IServiceCollection _services;
    private readonly ILogger<ConfigurationValidator>? _logger;
    private readonly List<ValidationResult> _results = new();
    
    public ConfigurationValidator(IServiceCollection services, ILogger<ConfigurationValidator>? logger = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _logger = logger;
    }
    
    /// <summary>
    /// Validates the entire configuration
    /// </summary>
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
/// Configuration validation result
/// </summary>
public class ValidationResult
{
    public ValidationSeverity Severity { get; }
    public string Message { get; }
    
    public ValidationResult(ValidationSeverity severity, string message)
    {
        Severity = severity;
        Message = message ?? throw new ArgumentNullException(nameof(message));
    }
}

/// <summary>
/// Validation severity levels
/// </summary>
public enum ValidationSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>
/// Validation report containing all validation results
/// </summary>
public class ValidationReport : IValidationReport
{
    private readonly List<ValidationResult> _results;
    
    public IReadOnlyList<ValidationResult> Results => _results;
    public bool IsValid => !_results.Any(r => r.Severity == ValidationSeverity.Error);
    public bool HasWarnings => _results.Any(r => r.Severity == ValidationSeverity.Warning);
    
    public IEnumerable<ValidationResult> Errors => _results.Where(r => r.Severity == ValidationSeverity.Error);
    public IEnumerable<ValidationResult> Warnings => _results.Where(r => r.Severity == ValidationSeverity.Warning);
    public IEnumerable<ValidationResult> Information => _results.Where(r => r.Severity == ValidationSeverity.Info);
    
    public ValidationReport(IEnumerable<ValidationResult> results)
    {
        _results = results?.ToList() ?? new List<ValidationResult>();
    }
    
    public override string ToString()
    {
        if (IsValid && !HasWarnings)
            return "Configuration is valid";
        
        var errors = Errors.Count();
        var warnings = Warnings.Count();
        
        return $"Configuration validation: {errors} error(s), {warnings} warning(s)";
    }
}