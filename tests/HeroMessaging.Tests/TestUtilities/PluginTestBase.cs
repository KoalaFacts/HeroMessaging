using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeroMessaging.Tests.TestUtilities;

public abstract class PluginTestBase<TPlugin> : IDisposable where TPlugin : class
{
    private readonly List<IDisposable> _disposables = new();
    private bool _disposed = false;
    protected IServiceProvider ServiceProvider { get; private set; } = null!;
    protected TPlugin Plugin { get; private set; } = null!;

    protected PluginTestBase()
    {
        SetupPlugin();
    }

    protected virtual void SetupPlugin()
    {
        var services = new ServiceCollection();
        ConfigureTestServices(services);

        ServiceProvider = services.BuildServiceProvider();
        Plugin = LoadPlugin();
        AssertPluginLoaded();
    }

    protected abstract void ConfigureTestServices(IServiceCollection services);

    protected virtual TPlugin LoadPlugin()
    {
        var pluginType = typeof(TPlugin);

        if (pluginType.IsInterface || pluginType.IsAbstract)
        {
            return ServiceProvider.GetService<TPlugin>()
                ?? throw new InvalidOperationException($"Plugin {typeof(TPlugin).Name} not registered in service container");
        }

        return Activator.CreateInstance<TPlugin>();
    }

    protected virtual Dictionary<string, object> GetTestConfiguration()
    {
        return new Dictionary<string, object>
        {
            ["TestMode"] = true,
            ["Timeout"] = TimeSpan.FromSeconds(30),
            ["MaxRetries"] = 3
        };
    }

    protected virtual void AssertPluginLoaded()
    {
        Assert.NotNull(Plugin);
    }

    protected virtual async Task TeardownPlugin()
    {
        if (Plugin is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else if (Plugin is IDisposable disposable)
        {
            disposable.Dispose();
        }

        foreach (var item in _disposables)
        {
            item.Dispose();
        }
        _disposables.Clear();

        if (ServiceProvider is IDisposable sp)
        {
            sp.Dispose();
        }
    }

    protected void RegisterForDisposal(IDisposable disposable)
    {
        _disposables.Add(disposable);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                TeardownPlugin().GetAwaiter().GetResult();
            }
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}

public class PluginLoadContext : AssemblyLoadContext
{
    private readonly string _pluginPath;
    private readonly Dictionary<string, Assembly> _loadedAssemblies = new();

    public PluginLoadContext(string pluginPath) : base(isCollectible: true)
    {
        _pluginPath = pluginPath;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (_loadedAssemblies.TryGetValue(assemblyName.FullName, out var cached))
        {
            return cached;
        }

        var assemblyPath = System.IO.Path.Combine(_pluginPath, $"{assemblyName.Name}.dll");

        if (System.IO.File.Exists(assemblyPath))
        {
            var assembly = LoadFromAssemblyPath(assemblyPath);
            _loadedAssemblies[assemblyName.FullName] = assembly;
            return assembly;
        }

        return null;
    }

    public T CreatePluginInstance<T>(string typeName) where T : class
    {
        var assembly = _loadedAssemblies.Values.FirstOrDefault(a =>
            a.GetTypes().Any(t => t.FullName == typeName));

        if (assembly == null)
        {
            throw new InvalidOperationException($"Type {typeName} not found in loaded assemblies");
        }

        var type = assembly.GetType(typeName);
        if (type == null)
        {
            throw new InvalidOperationException($"Type {typeName} not found");
        }

        return Activator.CreateInstance(type) as T
            ?? throw new InvalidOperationException($"Could not create instance of {typeName}");
    }

    public void SimulateVersionConflict(string assemblyName, Version version)
    {
        var assembly = _loadedAssemblies.Values.FirstOrDefault(a => a.GetName().Name == assemblyName);
        if (assembly != null)
        {
            var currentVersion = assembly.GetName().Version;
            if (currentVersion != version)
            {
                throw new InvalidOperationException(
                    $"Version conflict: {assemblyName} v{currentVersion} loaded, but v{version} requested");
            }
        }
    }
}