using System.Buffers.Binary;

namespace HeroMessaging.Benchmarks;

internal static class BatchMarkerPayload
{
    internal static readonly Guid ProviderId = new("2bfc1699-762e-5dae-62c6-95195cf93938");

    // Version one carries a batch identity; version zero cannot safely pair missing markers.
    internal static bool TryReadStart(Guid provider, int eventId, int version, ReadOnlySpan<byte> payload, int messages, out int batchId)
    {
        batchId = 0;
        if (provider != ProviderId || eventId != 1 || version != 1 || payload.Length != 16 || messages <= 0 ||
            BinaryPrimitives.ReadInt32LittleEndian(payload) != messages ||
            BinaryPrimitives.ReadInt32LittleEndian(payload[4..]) != 3 ||
            BinaryPrimitives.ReadInt32LittleEndian(payload[8..]) != 32)
            return false;
        batchId = BinaryPrimitives.ReadInt32LittleEndian(payload[12..]);
        return batchId > 0;
    }

    internal static bool TryReadStop(Guid provider, int eventId, int version, ReadOnlySpan<byte> payload, out int batchId)
    {
        batchId = 0;
        if (provider != ProviderId || eventId != 2 || version != 1 || payload.Length != 4)
            return false;
        batchId = BinaryPrimitives.ReadInt32LittleEndian(payload);
        return batchId > 0;
    }
}
