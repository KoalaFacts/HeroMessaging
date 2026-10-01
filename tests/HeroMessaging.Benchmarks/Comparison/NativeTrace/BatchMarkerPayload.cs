using System.Buffers.Binary;

namespace HeroMessaging.Benchmarks;

internal static class BatchMarkerPayload
{
    internal static readonly Guid ProviderId = new("2bfc1699-762e-5dae-62c6-95195cf93938");

    // Version zero is exactly three little-endian Int32 fields: messages, handlers, producers.
    internal static bool MatchesStart(Guid provider, int eventId, int version, ReadOnlySpan<byte> payload, int messages) =>
        provider == ProviderId && eventId == 1 && version == 0 && payload.Length == 12 && messages > 0 &&
        BinaryPrimitives.ReadInt32LittleEndian(payload) == messages &&
        BinaryPrimitives.ReadInt32LittleEndian(payload[4..]) == 3 &&
        BinaryPrimitives.ReadInt32LittleEndian(payload[8..]) == 32;

    internal static bool MatchesStop(Guid provider, int eventId, int version, ReadOnlySpan<byte> payload) =>
        provider == ProviderId && eventId == 2 && version == 0 && payload.IsEmpty;
}
