using HeroMessaging.Abstractions.Commands;

namespace HeroMessaging.Abstractions;

/// <summary>
/// Provides command sending capabilities.
/// </summary>
/// <remarks>
/// This interface is a subset of <see cref="IHeroMessaging"/> for consumers
/// that only need to send commands. Use this for dependency injection when
/// your component only needs command capabilities.
/// </remarks>
public interface ICommandSender
{
    /// <summary>
    /// Sends a fire-and-forget command to its registered handler.
    /// </summary>
    /// <param name="command">The command to send.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="command"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no handler is registered for the command type.</exception>
    Task SendAsync(ICommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a command and awaits its response from the registered handler.
    /// </summary>
    /// <typeparam name="TResponse">The type of response expected from the command handler.</typeparam>
    /// <param name="command">The command to send.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The response from the command handler.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="command"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no handler is registered for the command type.</exception>
    Task<TResponse> SendAsync<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends multiple fire-and-forget commands in a batch operation.
    /// </summary>
    /// <param name="commands">The commands to send.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of boolean values indicating success (true) or failure (false) for each command.</returns>
    /// <remarks>
    /// Failed commands do not stop processing of remaining commands. The operation continues
    /// until all commands are processed or the cancellation token is triggered.
    /// </remarks>
    Task<IReadOnlyList<bool>> SendBatchAsync(IReadOnlyList<ICommand> commands, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends multiple commands with responses in a batch operation.
    /// </summary>
    /// <typeparam name="TResponse">The type of response expected from each command handler.</typeparam>
    /// <param name="commands">The commands to send.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of responses from the command handlers.</returns>
    Task<IReadOnlyList<TResponse>> SendBatchAsync<TResponse>(IReadOnlyList<ICommand<TResponse>> commands, CancellationToken cancellationToken = default);
}
