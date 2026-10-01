namespace HeroMessaging.Benchmarks;

internal readonly record struct BatchWindow(int BatchId, double Start, double Stop);

internal sealed class BatchWindowCollector
{
    private readonly List<BatchWindow> _windows = [];
    private int _latestBatchId;
    private (int BatchId, double Timestamp)? _pending;

    internal IReadOnlyList<BatchWindow> Windows => _windows;

    internal void Observe(Guid provider, int eventId, ReadOnlySpan<byte> payload, int messages, double timestamp)
    {
        if (provider != BatchMarkerPayload.ProviderId || eventId is not (1 or 2))
            return;
        if (!double.IsFinite(timestamp) || timestamp < 0)
        {
            _pending = null;
            return;
        }
        if (eventId == 1)
        {
            _pending = null;
            if (!BatchMarkerPayload.TryReadStart(provider, eventId, payload, messages, out var batchId))
                return;
            if (batchId <= _latestBatchId)
            {
                _windows.RemoveAll(window => window.BatchId == batchId);
                return;
            }
            _latestBatchId = batchId;
            _pending = (batchId, timestamp);
            return;
        }

        if (BatchMarkerPayload.TryReadStop(provider, eventId, payload, out var stoppedId))
        {
            // A stop from a later batch must not close an earlier start after marker loss.
            if (_pending is { } start && start.BatchId == stoppedId && timestamp > start.Timestamp)
                _windows.Add(new BatchWindow(stoppedId, start.Timestamp, timestamp));
            _latestBatchId = Math.Max(_latestBatchId, stoppedId);
        }
        _pending = null;
    }
}
