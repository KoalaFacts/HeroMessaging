using System.Runtime.ExceptionServices;
using System.Threading.Channels;

namespace HeroMessaging.Processing;

// Admission permits cover both queued and executing deliveries, like ActionBlock's bounded capacity.
internal sealed class EventDispatchQueue<T>
{
    private readonly Channel<T> _channel;
    private readonly SemaphoreSlim _slots;
    private readonly CancellationTokenSource _admissionStopped = new();
    private readonly Func<T, Task> _process;
    private readonly Action<T> _discard;
    private Exception? _failure;
    private int _stopped;

    internal EventDispatchQueue(int capacity, int parallelism, Func<T, Task> process, Action<T> discard)
    {
        _process = process;
        _discard = discard;
        _slots = new SemaphoreSlim(capacity, capacity);
        var workers = new Task[Math.Min(capacity, parallelism)];
        _channel = Channel.CreateUnbounded<T>(new UnboundedChannelOptions
        {
            SingleReader = workers.Length == 1,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        for (var index = 0; index < workers.Length; index++)
            workers[index] = RunWorkerAsync();
        Completion = FinishAsync(workers);
    }

    internal Task Completion { get; }

    internal Task<bool> SendAsync(T item, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<bool>(cancellationToken);
        if (Volatile.Read(ref _stopped) != 0)
            return Task.FromResult(false);
        if (_slots.Wait(0))
            return Task.FromResult(TryEnqueue(item));
        return WaitForAdmissionAsync(item, cancellationToken);
    }

    private async Task<bool> WaitForAdmissionAsync(T item, CancellationToken cancellationToken)
    {
        using var linked = cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _admissionStopped.Token)
            : null;
        try
        {
            await _slots.WaitAsync(linked?.Token ?? _admissionStopped.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (OperationCanceledException)
        {
            // Do not expose the internal linked token through the public publication task.
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        return TryEnqueue(item);
    }

    private bool TryEnqueue(T item)
    {
        if (_channel.Writer.TryWrite(item)) return true;
        _slots.Release();
        return false;
    }

    internal void Complete()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0) return;
        _channel.Writer.TryComplete();
        _admissionStopped.Cancel();
    }

    private async Task RunWorkerAsync()
    {
        while (await _channel.Reader.WaitToReadAsync().ConfigureAwait(false))
        {
            while (_channel.Reader.TryRead(out var item))
            {
                try
                {
                    if (Volatile.Read(ref _failure) is null)
                        await _process(item).ConfigureAwait(false);
                    else
                        _discard(item);
                }
                catch (OperationCanceledException)
                {
                    // Cooperative handler cancellation does not fault the dispatcher.
                }
                catch (Exception exception)
                {
                    Interlocked.CompareExchange(ref _failure, exception, null);
                    Complete();
                }
                finally { _slots.Release(); }
            }
        }
    }

    private async Task FinishAsync(Task[] workers)
    {
        await Task.WhenAll(workers).ConfigureAwait(false);
        if (_failure is { } exception) ExceptionDispatchInfo.Capture(exception).Throw();
    }
}
