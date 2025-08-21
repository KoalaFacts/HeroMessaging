using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Abstractions.Versioning;

/// <summary>
/// Represents a message with versioning support
/// </summary>
public interface IVersionedMessage : IMessage
{
    /// <summary>
    /// Gets the message schema version
    /// </summary>
    MessageVersion Version { get; }
    
    /// <summary>
    /// Gets the message type name for version identification
    /// </summary>
    string MessageType { get; }
}

/// <summary>
/// Represents a message with versioning support and response type
/// </summary>
/// <typeparam name="TResponse">The response type</typeparam>
public interface IVersionedMessage<TResponse> : IVersionedMessage, IMessage<TResponse>
{
}

/// <summary>
/// Represents a message version using semantic versioning
/// </summary>
public readonly record struct MessageVersion
{
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    
    public MessageVersion(int major, int minor = 0, int patch = 0)
    {
        if (major < 0) throw new ArgumentOutOfRangeException(nameof(major), "Major version cannot be negative");
        if (minor < 0) throw new ArgumentOutOfRangeException(nameof(minor), "Minor version cannot be negative");  
        if (patch < 0) throw new ArgumentOutOfRangeException(nameof(patch), "Patch version cannot be negative");
        
        Major = major;
        Minor = minor;
        Patch = patch;
    }

    /// <summary>
    /// Parses a version string in format "major.minor.patch"
    /// </summary>
    public static MessageVersion Parse(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("Version string cannot be null or empty", nameof(version));

        var parts = version.Split('.');
        if (parts.Length < 1 || parts.Length > 3)
            throw new FormatException($"Invalid version format: {version}");

        if (!int.TryParse(parts[0], out var major))
            throw new FormatException($"Invalid major version: {parts[0]}");

        var minor = parts.Length > 1 && int.TryParse(parts[1], out var m) ? m : 0;
        var patch = parts.Length > 2 && int.TryParse(parts[2], out var p) ? p : 0;

        return new MessageVersion(major, minor, patch);
    }

    /// <summary>
    /// Tries to parse a version string
    /// </summary>
    public static bool TryParse(string version, out MessageVersion messageVersion)
    {
        try
        {
            messageVersion = Parse(version);
            return true;
        }
        catch
        {
            messageVersion = default;
            return false;
        }
    }

    /// <summary>
    /// Checks if this version is compatible with another version for message handling
    /// Compatible means same major version and this version >= other version
    /// </summary>
    public bool IsCompatibleWith(MessageVersion other)
    {
        // Different major versions are not compatible
        if (Major != other.Major)
            return false;
        
        // Same major version - check if this version is >= other version
        if (Minor > other.Minor)
            return true;
        
        if (Minor == other.Minor)
            return Patch >= other.Patch;
        
        return false;
    }

    /// <summary>
    /// Checks if this version can handle messages of the specified version
    /// (i.e., this version >= specified version with same major)
    /// </summary>
    public bool CanHandle(MessageVersion messageVersion)
    {
        return IsCompatibleWith(messageVersion) && this >= messageVersion;
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}";

    public static implicit operator string(MessageVersion version) => version.ToString();
    public static explicit operator MessageVersion(string version) => Parse(version);

    public static bool operator >(MessageVersion left, MessageVersion right) => left.CompareTo(right) > 0;
    public static bool operator >=(MessageVersion left, MessageVersion right) => left.CompareTo(right) >= 0;
    public static bool operator <(MessageVersion left, MessageVersion right) => left.CompareTo(right) < 0;
    public static bool operator <=(MessageVersion left, MessageVersion right) => left.CompareTo(right) <= 0;

    public int CompareTo(MessageVersion other)
    {
        var majorComparison = Major.CompareTo(other.Major);
        if (majorComparison != 0) return majorComparison;
        
        var minorComparison = Minor.CompareTo(other.Minor);
        if (minorComparison != 0) return minorComparison;
        
        return Patch.CompareTo(other.Patch);
    }
}

/// <summary>
/// Attribute to specify the version of a message type
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class MessageVersionAttribute : Attribute
{
    public MessageVersion Version { get; }
    
    public MessageVersionAttribute(int major, int minor = 0, int patch = 0)
    {
        Version = new MessageVersion(major, minor, patch);
    }
    
    public MessageVersionAttribute(string version)
    {
        Version = MessageVersion.Parse(version);
    }
}

/// <summary>
/// Attribute to mark properties that were added in a specific version
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public class AddedInVersionAttribute : Attribute
{
    public MessageVersion Version { get; }
    
    public AddedInVersionAttribute(int major, int minor = 0, int patch = 0)
    {
        Version = new MessageVersion(major, minor, patch);
    }
    
    public AddedInVersionAttribute(string version)
    {
        Version = MessageVersion.Parse(version);
    }
}

/// <summary>
/// Attribute to mark properties that were deprecated in a specific version
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public class DeprecatedInVersionAttribute : Attribute
{
    public MessageVersion Version { get; }
    public string? Reason { get; }
    public string? ReplacedBy { get; }
    
    public DeprecatedInVersionAttribute(int major, int minor = 0, int patch = 0)
    {
        Version = new MessageVersion(major, minor, patch);
    }
    
    public DeprecatedInVersionAttribute(string version)
    {
        Version = MessageVersion.Parse(version);
    }
    
    public DeprecatedInVersionAttribute(string version, string reason, string? replacedBy = null)
        : this(version)
    {
        Reason = reason;
        ReplacedBy = replacedBy;
    }
}