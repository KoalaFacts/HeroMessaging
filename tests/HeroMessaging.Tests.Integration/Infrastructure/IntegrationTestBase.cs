using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using Xunit;

namespace HeroMessaging.Tests.Integration.Infrastructure;

public abstract class IntegrationTestBase : IAsyncLifetime
{
    protected IServiceProvider ServiceProvider { get; private set; } = null!;
    protected IServiceCollection Services { get; private set; } = null!;

    public virtual async ValueTask InitializeAsync()
    {
        Services = new ServiceCollection();
        
        // Add logging
        Services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        // Configure services for testing
        await ConfigureServicesAsync(Services);
        
        ServiceProvider = Services.BuildServiceProvider();
        
        await OnInitializedAsync();
    }

    public virtual async ValueTask DisposeAsync()
    {
        await OnDisposingAsync();
        
        if (ServiceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    protected virtual Task ConfigureServicesAsync(IServiceCollection services)
    {
        return Task.CompletedTask;
    }

    protected virtual Task OnInitializedAsync()
    {
        return Task.CompletedTask;
    }

    protected virtual Task OnDisposingAsync()
    {
        return Task.CompletedTask;
    }

    protected T GetRequiredService<T>() where T : notnull
    {
        return ServiceProvider.GetRequiredService<T>();
    }
}