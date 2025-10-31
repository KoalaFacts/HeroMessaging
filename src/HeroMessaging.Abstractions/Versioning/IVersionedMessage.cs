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
    /// <summary>
    /// Gets the major version number.
    /// Incremented for breaking changes that are not backwards compatible.
    /// </summary>
    public int Major { get; }

    /// <summary>
    /// Gets the minor version number.
    /// Incremented for new features that are backwards compatible.
    /// </summary>
    public int Minor { get; }

    /// <summary>
    /// Gets the patch version number.
    /// Incremented for backwards compatible bug fixes.
    /// </summary>
    public int Patch { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageVersion"/> struct.
    /// </summary>
    /// <param name="major">The major version number (must be non-negative).</param>
    /// <param name="minor">The minor version number (must be non-negative, default is 0).</param>
    /// <param name="patch">The patch version number (must be non-negative, default is 0).</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="major"/>, <paramref name="minor"/>, or <paramref name="patch"/> is negative.
    /// </exception>
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

    /// <summary>
    /// Returns a string representation of the message version in the format "Major.Minor.Patch".
    /// </summary>
    /// <returns>A string in the format "Major.Minor.Patch" (e.g., "1.2.3").</returns>
    public override string ToString() => $"{Major}.{Minor}.{Patch}";

    /// <summary>
    /// Implicitly converts a <see cref="MessageVersion"/> to its string representation.
    /// </summary>
    /// <param name="version">The message version to convert.</param>
    /// <returns>A string in the format "Major.Minor.Patch".</returns>
    public static implicit operator string(MessageVersion version) => version.ToString();

    /// <summary>
    /// Explicitly converts a string to a <see cref="MessageVersion"/>.
    /// </summary>
    /// <param name="version">The version string to parse (e.g., "1.2.3").</param>
    /// <returns>A <see cref="MessageVersion"/> instance.</returns>
    /// <exception cref="FormatException">Thrown when the version string format is invalid.</exception>
    /// <exception cref="ArgumentException">Thrown when the version string is null or empty.</exception>
    public static explicit operator MessageVersion(string version) => Parse(version);

    /// <summary>
    /// Determines whether one message version is greater than another.
    /// </summary>
    /// <param name="left">The first version to compare.</param>
    /// <param name="right">The second version to compare.</param>
    /// <returns><c>true</c> if <paramref name="left"/> is greater than <paramref name="right"/>; otherwise, <c>false</c>.</returns>
    public static bool operator >(MessageVersion left, MessageVersion right) => left.CompareTo(right) > 0;

    /// <summary>
    /// Determines whether one message version is greater than or equal to another.
    /// </summary>
    /// <param name="left">The first version to compare.</param>
    /// <param name="right">The second version to compare.</param>
    /// <returns><c>true</c> if <paramref name="left"/> is greater than or equal to <paramref name="right"/>; otherwise, <c>false</c>.</returns>
    public static bool operator >=(MessageVersion left, MessageVersion right) => left.CompareTo(right) >= 0;

    /// <summary>
    /// Determines whether one message version is less than another.
    /// </summary>
    /// <param name="left">The first version to compare.</param>
    /// <param name="right">The second version to compare.</param>
    /// <returns><c>true</c> if <paramref name="left"/> is less than <paramref name="right"/>; otherwise, <c>false</c>.</returns>
    public static bool operator <(MessageVersion left, MessageVersion right) => left.CompareTo(right) < 0;

    /// <summary>
    /// Determines whether one message version is less than or equal to another.
    /// </summary>
    /// <param name="left">The first version to compare.</param>
    /// <param name="right">The second version to compare.</param>
    /// <returns><c>true</c> if <paramref name="left"/> is less than or equal to <paramref name="right"/>; otherwise, <c>false</c>.</returns>
    public static bool operator <=(MessageVersion left, MessageVersion right) => left.CompareTo(right) <= 0;

    /// <summary>
    /// Compares this message version to another message version.
    /// </summary>
    /// <param name="other">The message version to compare with this instance.</param>
    /// <returns>
    /// A negative value if this version is less than <paramref name="other"/>,
    /// zero if they are equal,
    /// or a positive value if this version is greater than <paramref name="other"/>.
    /// </returns>
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
public sealed class MessageVersionAttribute(int major, int minor = 0, int patch = 0) : Attribute
{
    /// <summary>
    /// Gets the major version number.
    /// </summary>
    public int Major { get; } = major;

    /// <summary>
    /// Gets the minor version number.
    /// </summary>
    public int Minor { get; } = minor;

    /// <summary>
    /// Gets the patch version number.
    /// </summary>
    public int Patch { get; } = patch;

    /// <summary>
    /// Gets the message version represented by this attribute.
    /// </summary>
    public MessageVersion Version { get; } = new MessageVersion(major, minor, patch);
}

/// <summary>
/// Attribute to mark properties that were added in a specific version
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class AddedInVersionAttribute(int major, int minor = 0, int patch = 0) : Attribute
{
    /// <summary>
    /// Gets the major version number when this property was added.
    /// </summary>
    public int Major { get; } = major;

    /// <summary>
    /// Gets the minor version number when this property was added.
    /// </summary>
    public int Minor { get; } = minor;

    /// <summary>
    /// Gets the patch version number when this property was added.
    /// </summary>
    public int Patch { get; } = patch;

    /// <summary>
    /// Gets the version when this property was added.
    /// </summary>
    public MessageVersion Version { get; } = new MessageVersion(major, minor, patch);
}

/// <summary>
/// Attribute to mark properties that were deprecated in a specific version
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class DeprecatedInVersionAttribute : Attribute
{
    /// <summary>
    /// Gets the major version number when this property was deprecated.
    /// </summary>
    public int Major { get; }

    /// <summary>
    /// Gets the minor version number when this property was deprecated.
    /// </summary>
    public int Minor { get; }

    /// <summary>
    /// Gets the patch version number when this property was deprecated.
    /// </summary>
    public int Patch { get; }

    /// <summary>
    /// Gets the version when this property was deprecated.
    /// </summary>
    public MessageVersion Version { get; }

    /// <summary>
    /// Gets or sets the reason why this property was deprecated.
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Gets or sets the name of the property or feature that replaces this deprecated property.
    /// </summary>
    public string? ReplacedBy { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DeprecatedInVersionAttribute"/> class.
    /// </summary>
    /// <param name="major">The major version number when the property was deprecated.</param>
    /// <param name="minor">The minor version number when the property was deprecated (default is 0).</param>
    /// <param name="patch">The patch version number when the property was deprecated (default is 0).</param>
    public DeprecatedInVersionAttribute(int major, int minor = 0, int patch = 0)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Version = new MessageVersion(major, minor, patch);
    }
}