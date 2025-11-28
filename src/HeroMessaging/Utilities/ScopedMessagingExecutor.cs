using HeroMessaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace HeroMessaging.Utilities;

/// <summary>
/// Utility for executing messaging operations within a scoped service context.
/// Consolidates the common pattern of creating a scope and resolving IHeroMessaging.
/// </summary>
public static class ScopedMessagingExecutor
{
    /// <summary>
    /// Executes an action with IHeroMessaging within a scoped service context.
    /// </summary>
    /// <param name="serviceProvider">The root service provider</param>
    /// <param name="action">The action to execute with the messaging service</param>
    public static async Task ExecuteAsync(
        IServiceProvider serviceProvider,
        Func<IHeroMessaging, Task> action)
    {
        using var scope = serviceProvider.CreateScope();
        var messaging = scope.ServiceProvider.GetRequiredService<IHeroMessaging>();
        await action(messaging).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes a function with IHeroMessaging within a scoped service context and returns a result.
    /// </summary>
    /// <typeparam name="T">The return type</typeparam>
    /// <param name="serviceProvider">The root service provider</param>
    /// <param name="func">The function to execute with the messaging service</param>
    /// <returns>The result of the function</returns>
    public static async Task<T> ExecuteAsync<T>(
        IServiceProvider serviceProvider,
        Func<IHeroMessaging, Task<T>> func)
    {
        using var scope = serviceProvider.CreateScope();
        var messaging = scope.ServiceProvider.GetRequiredService<IHeroMessaging>();
        return await func(messaging).ConfigureAwait(false);
    }

    /// <summary>
    /// Dispatches a message within a scoped service context.
    /// Combines scope creation with MessageDispatcher.
    /// </summary>
    /// <param name="serviceProvider">The root service provider</param>
    /// <param name="message">The message to dispatch</param>
    /// <param name="logger">Optional logger for warnings</param>
    /// <param name="source">Optional source identifier for logging</param>
    /// <returns>True if the message was successfully dispatched</returns>
    public static async Task<bool> DispatchAsync(
        IServiceProvider serviceProvider,
        Abstractions.Messages.IMessage message,
        Microsoft.Extensions.Logging.ILogger? logger = null,
        string? source = null)
    {
        using var scope = serviceProvider.CreateScope();
        var messaging = scope.ServiceProvider.GetRequiredService<IHeroMessaging>();
        return await MessageDispatcher.DispatchAsync(messaging, message, logger, source).ConfigureAwait(false);
    }
}
