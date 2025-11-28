using HeroMessaging.Abstractions.Plugins;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Plugins;

/// <summary>
/// Service for loading and initializing plugins
/// </summary>
public class PluginLoader : IPluginLoader
{
    private readonly ILogger<PluginLoader>? _logger;

    public PluginLoader(ILogger<PluginLoader>? logger = null)
    {
        _logger = logger;
    }

    public async Task<IMessagingPlugin> LoadAsync(
        IPluginDescriptor descriptor,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        return await LoadAsync(descriptor, serviceProvider, null, cancellationToken);
    }

    public async Task<IMessagingPlugin> LoadAsync(
        IPluginDescriptor descriptor,
        IServiceProvider serviceProvider,
        Action<object>? configure,
        CancellationToken cancellationToken = default)
    {
        if (descriptor == null)
            throw new ArgumentNullException(nameof(descriptor));
        if (serviceProvider == null)
            throw new ArgumentNullException(nameof(serviceProvider));

        _logger?.LogInformation("Loading plugin: {PluginName} v{Version}", descriptor.Name, descriptor.Version);

        try
        {
            // Validate before loading
            var validation = await ValidateAsync(descriptor, cancellationToken);
            if (!validation.IsValid)
            {
                var errors = string.Join(", ", validation.Errors);
                throw new InvalidOperationException($"Plugin validation failed: {errors}");
            }

            // Create instance
            var plugin = CreateInstance(descriptor, serviceProvider);

            // Apply configuration if provided
            if (configure != null && plugin != null)
            {
                configure(plugin);
            }

            _logger?.LogInformation("Successfully loaded plugin: {PluginName}", descriptor.Name);
            return plugin!; // Plugin is validated to be non-null
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load plugin: {PluginName}", descriptor.Name);
            throw;
        }
    }

    public Task<bool> CanLoadAsync(
        IPluginDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        if (descriptor == null)
            return Task.FromResult(false);

        try
        {
            // Check if type can be instantiated
            if (descriptor.PluginType.IsAbstract || descriptor.PluginType.IsInterface)
                return Task.FromResult(false);

            // Check if it implements IMessagingPlugin
            if (!typeof(IMessagingPlugin).IsAssignableFrom(descriptor.PluginType))
                return Task.FromResult(false);

            // Check if constructor exists
            var constructors = descriptor.PluginType.GetConstructors();
            return Task.FromResult(constructors.Length > 0);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Plugin {PluginName} cannot be loaded", descriptor?.Name ?? "unknown");
            return Task.FromResult(false);
        }
    }

    public Task<PluginValidationResult> ValidateAsync(
        IPluginDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        var errors = new System.Collections.Generic.List<string>();
        var warnings = new System.Collections.Generic.List<string>();
        var isValid = true;

        if (descriptor == null)
        {
            errors.Add("Plugin descriptor is null");
            isValid = false;
        }
        else
        {
            // Validate type
            if (descriptor.PluginType == null)
            {
                errors.Add("Plugin type is null");
                isValid = false;
            }
            else if (!typeof(IMessagingPlugin).IsAssignableFrom(descriptor.PluginType))
            {
                errors.Add($"Plugin type {descriptor.PluginType.Name} does not implement IMessagingPlugin");
                isValid = false;
            }

            // Validate name
            if (string.IsNullOrEmpty(descriptor.Name))
            {
                errors.Add("Plugin name is empty");
                isValid = false;
            }

            // Check for constructor
            if (descriptor.PluginType != null)
            {
                var constructors = descriptor.PluginType.GetConstructors();
                if (constructors.Length == 0)
                {
                    errors.Add("Plugin type has no public constructors");
                    isValid = false;
                }
            }

            // Warn about missing description
            if (string.IsNullOrEmpty(descriptor.Description))
            {
                warnings.Add("Plugin has no description");
            }
        }

        var result = new PluginValidationResult
        {
            IsValid = isValid,
            Errors = errors.ToArray(),
            Warnings = warnings.ToArray()
        };

        return Task.FromResult(result);
    }

    private IMessagingPlugin CreateInstance(IPluginDescriptor descriptor, IServiceProvider serviceProvider)
    {
        // Try to create using DI first
        var plugin = serviceProvider.GetService(descriptor.PluginType) as IMessagingPlugin;
        if (plugin != null)
        {
            return plugin;
        }

        // Try to create using activator with DI parameters
        var constructors = descriptor.PluginType.GetConstructors();
        if (constructors.Length == 0)
        {
            throw new InvalidOperationException($"Plugin type {descriptor.PluginType.Name} has no public constructors");
        }

        // Use the constructor with most parameters (assumed to be the primary one)
        var constructor = constructors.OrderByDescending(c => c.GetParameters().Length).First();
        var parameters = constructor.GetParameters();
        var args = new object?[parameters.Length];

        for (int i = 0; i < parameters.Length; i++)
        {
            var param = parameters[i];

            // Try to resolve from DI
            args[i] = serviceProvider.GetService(param.ParameterType);

            // Use default value if available and service not found
            if (args[i] == null && param.HasDefaultValue)
            {
                args[i] = param.DefaultValue;
            }
            else if (args[i] == null && !param.ParameterType.IsValueType)
            {
                // For reference types, we can pass null if not required
                args[i] = null;
            }
            else if (args[i] == null)
            {
                // For value types without default, create default instance
                args[i] = Activator.CreateInstance(param.ParameterType);
            }
        }

        var instance = Activator.CreateInstance(descriptor.PluginType, args);
        if (instance is not IMessagingPlugin messagingPlugin)
        {
            throw new InvalidOperationException($"Created instance of {descriptor.PluginType.Name} does not implement IMessagingPlugin");
        }

        return messagingPlugin;
    }
}