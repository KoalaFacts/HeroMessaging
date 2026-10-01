using System.Buffers.Binary;

namespace HeroMessaging.Benchmarks;

internal static class BatchMarkerPayload
{
    internal const int SchemaVersion = 2;
    internal static readonly Guid ProviderId = new("2bfc1699-762e-5dae-62c6-95195cf93938");

    // The schema version is in the payload: native metadata can omit the event-header version.
    internal static bool TryReadStart(Guid provider, int eventId, ReadOnlySpan<byte> payload, int messages, out int batchId)
    {
        batchId = 0;
        if (provider != ProviderId || eventId != 1 || payload.Length != 20 || messages <= 0 ||
            BinaryPrimitives.ReadInt32LittleEndian(payload) != SchemaVersion ||
            BinaryPrimitives.ReadInt32LittleEndian(payload[4..]) != messages ||
            BinaryPrimitives.ReadInt32LittleEndian(payload[8..]) != 3 ||
            BinaryPrimitives.ReadInt32LittleEndian(payload[12..]) != 32)
            return false;
        batchId = BinaryPrimitives.ReadInt32LittleEndian(payload[16..]);
        return batchId > 0;
    }

    internal static bool TryReadStop(Guid provider, int eventId, ReadOnlySpan<byte> payload, out int batchId)
    {
        batchId = 0;
        if (provider != ProviderId || eventId != 2 || payload.Length != 8 ||
            BinaryPrimitives.ReadInt32LittleEndian(payload) != SchemaVersion)
            return false;
        batchId = BinaryPrimitives.ReadInt32LittleEndian(payload[4..]);
        return batchId > 0;
    }
}
