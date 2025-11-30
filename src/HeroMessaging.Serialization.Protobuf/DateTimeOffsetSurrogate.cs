using ProtoBuf;

namespace HeroMessaging.Serialization.Protobuf;

/// <summary>
/// Surrogate for serializing DateTimeOffset with protobuf-net.
/// Converts DateTimeOffset to/from a format that protobuf can handle.
/// </summary>
[ProtoContract]
internal struct DateTimeOffsetSurrogate
{
    /// <summary>
    /// Gets or sets the UTC ticks
    /// </summary>
    [ProtoMember(1)]
    public long UtcTicks { get; set; }

    /// <summary>
    /// Gets or sets the offset in minutes
    /// </summary>
    [ProtoMember(2)]
    public short OffsetMinutes { get; set; }

    /// <summary>
    /// Converts DateTimeOffset to surrogate
    /// </summary>
    public static implicit operator DateTimeOffsetSurrogate(DateTimeOffset value)
    {
        return new DateTimeOffsetSurrogate
        {
            UtcTicks = value.UtcTicks,
            OffsetMinutes = (short)value.Offset.TotalMinutes
        };
    }

    /// <summary>
    /// Converts surrogate to DateTimeOffset
    /// </summary>
    public static implicit operator DateTimeOffset(DateTimeOffsetSurrogate value)
    {
        var offset = TimeSpan.FromMinutes(value.OffsetMinutes);
        return new DateTimeOffset(value.UtcTicks, TimeSpan.Zero).ToOffset(offset);
    }
}
