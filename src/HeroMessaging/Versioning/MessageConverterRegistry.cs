using System.Collections.Concurrent;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Versioning;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Versioning;

/// <summary>
/// Registry for managing message converters and finding conversion paths
/// </summary>
public class MessageConverterRegistry(ILogger<MessageConverterRegistry> logger) : IMessageConverterRegistry
{
    private readonly ILogger<MessageConverterRegistry> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly ConcurrentDictionary<Type, List<IMessageConverter>> _converters = new();
    private readonly ConcurrentDictionary<ConversionKey, MessageConversionPath?> _pathCache = new();


    /// <summary>
    /// Registers a message converter
    /// </summary>
    public void RegisterConverter<TMessage>(IMessageConverter<TMessage> converter) where TMessage : class, IMessage
    {
        if (converter == null) throw new ArgumentNullException(nameof(converter));

        var messageType = typeof(TMessage);
        var converterList = _converters.GetOrAdd(messageType, _ => []);

        lock (converterList)
        {
            // Check for duplicate or overlapping converters
            var existingConverter = converterList.FirstOrDefault(c =>
                c.SupportedVersionRange.Overlaps(converter.SupportedVersionRange));

            if (existingConverter != null)
            {
                _logger.LogWarning("Overlapping converter registered for {MessageType}. " +
                    "Existing: {ExistingRange}, New: {NewRange}",
                    messageType.Name, existingConverter.SupportedVersionRange, converter.SupportedVersionRange);
            }

            converterList.Add(converter);
            _logger.LogInformation("Registered converter for {MessageType} supporting versions {VersionRange}",
                messageType.Name, converter.SupportedVersionRange);
        }

        // Clear path cache since we have new converters
        ClearPathCache();
    }

    /// <summary>
    /// Gets a converter for the specified message type and version range
    /// </summary>
    public IMessageConverter<TMessage>? GetConverter<TMessage>(MessageVersion fromVersion, MessageVersion toVersion)
        where TMessage : class, IMessage
    {
        var converter = GetConverter(typeof(TMessage), fromVersion, toVersion);
        return converter as IMessageConverter<TMessage>;
    }

    /// <summary>
    /// Gets a converter for the specified message type and version range
    /// </summary>
    public IMessageConverter? GetConverter(Type messageType, MessageVersion fromVersion, MessageVersion toVersion)
    {
        if (!_converters.TryGetValue(messageType, out var converterList))
            return null;

        lock (converterList)
        {
            return converterList.FirstOrDefault(c => c.CanConvert(messageType, fromVersion, toVersion));
        }
    }

    /// <summary>
    /// Gets all registered converters for a message type
    /// </summary>
    public IEnumerable<IMessageConverter> GetConverters(Type messageType)
    {
        if (_converters.TryGetValue(messageType, out var converterList))
        {
            lock (converterList)
            {
                return converterList.ToList(); // Return a copy to avoid concurrency issues
            }
        }

        return [];
    }

    /// <summary>
    /// Checks if conversion is possible between versions
    /// </summary>
    public bool CanConvert(Type messageType, MessageVersion fromVersion, MessageVersion toVersion)
    {
        return FindConversionPath(messageType, fromVersion, toVersion) != null;
    }

    /// <summary>
    /// Finds the conversion path between two versions (may involve multiple steps)
    /// </summary>
    public MessageConversionPath? FindConversionPath(Type messageType, MessageVersion fromVersion, MessageVersion toVersion)
    {
        var key = new ConversionKey(messageType, fromVersion, toVersion);

        return _pathCache.GetOrAdd(key, _ =>
        {
            return FindConversionPathInternal(messageType, fromVersion, toVersion);
        });
    }

    private MessageConversionPath? FindConversionPathInternal(Type messageType, MessageVersion fromVersion, MessageVersion toVersion)
    {
        // If versions are the same, no conversion needed
        if (fromVersion == toVersion)
        {
            return new MessageConversionPath(messageType, fromVersion, toVersion, Array.Empty<MessageConversionStep>());
        }

        var converters = GetConverters(messageType).ToList();
        if (!converters.Any())
        {
            _logger.LogDebug("No converters found for message type {MessageType}", messageType.Name);
            return null;
        }

        // Try direct conversion first
        var directConverter = converters.FirstOrDefault(c => c.CanConvert(messageType, fromVersion, toVersion));
        if (directConverter != null)
        {
            _logger.LogDebug("Found direct conversion path for {MessageType} from {FromVersion} to {ToVersion}",
                messageType.Name, fromVersion, toVersion);

            return new MessageConversionPath(
                messageType,
                fromVersion,
                toVersion,
                new[] { new MessageConversionStep(fromVersion, toVersion, directConverter) }
            );
        }

        // Try multi-step conversion using Dijkstra's algorithm
        return FindShortestConversionPath(messageType, fromVersion, toVersion, converters);
    }

    private MessageConversionPath? FindShortestConversionPath(
        Type messageType,
        MessageVersion fromVersion,
        MessageVersion toVersion,
        List<IMessageConverter> converters)
    {
        // Build version graph
        var allVersions = new HashSet<MessageVersion> { fromVersion, toVersion };
        foreach (var converter in converters)
        {
            allVersions.Add(converter.SupportedVersionRange.MinVersion);
            allVersions.Add(converter.SupportedVersionRange.MaxVersion);
        }

        // Use modified Dijkstra's algorithm to find shortest path
        var distances = new Dictionary<MessageVersion, int>();
        var previous = new Dictionary<MessageVersion, (MessageVersion prev, IMessageConverter converter)>();
        var unvisited = new SortedSet<(int distance, MessageVersion version)>();

        // Initialize distances
        foreach (var version in allVersions)
        {
            var distance = version == fromVersion ? 0 : int.MaxValue;
            distances[version] = distance;
            unvisited.Add((distance, version));
        }

        while (unvisited.Count > 0)
        {
            var (currentDistance, currentVersion) = unvisited.Min;
            unvisited.Remove((currentDistance, currentVersion));

            if (currentVersion == toVersion)
                break;

            if (currentDistance == int.MaxValue)
                break;

            // Check all possible conversions from current version
            foreach (var converter in converters)
            {
                var possibleTargets = GetPossibleConversions(currentVersion, converter);

                foreach (var targetVersion in possibleTargets)
                {
                    if (!distances.ContainsKey(targetVersion))
                        continue;

                    var newDistance = currentDistance + 1; // Each conversion step costs 1

                    if (newDistance < distances[targetVersion])
                    {
                        unvisited.Remove((distances[targetVersion], targetVersion));
                        distances[targetVersion] = newDistance;
                        previous[targetVersion] = (currentVersion, converter);
                        unvisited.Add((newDistance, targetVersion));
                    }
                }
            }
        }

        // Reconstruct path
        if (!previous.ContainsKey(toVersion))
        {
            _logger.LogDebug("No conversion path found for {MessageType} from {FromVersion} to {ToVersion}",
                messageType.Name, fromVersion, toVersion);
            return null;
        }

        var steps = new List<MessageConversionStep>();
        var current = toVersion;

        while (previous.ContainsKey(current))
        {
            var (prev, converter) = previous[current];
            steps.Insert(0, new MessageConversionStep(prev, current, converter));
            current = prev;
        }

        _logger.LogDebug("Found {StepCount}-step conversion path for {MessageType} from {FromVersion} to {ToVersion}",
            steps.Count, messageType.Name, fromVersion, toVersion);

        return new MessageConversionPath(messageType, fromVersion, toVersion, steps);
    }

    private static IEnumerable<MessageVersion> GetPossibleConversions(MessageVersion fromVersion, IMessageConverter converter)
    {
        var range = converter.SupportedVersionRange;

        // If the converter supports the from version, it can convert to any version in its range
        if (range.Contains(fromVersion))
        {
            yield return range.MinVersion;
            yield return range.MaxVersion;

            // In a more sophisticated implementation, we could enumerate all known versions in the range
        }
    }

    private void ClearPathCache()
    {
        _pathCache.Clear();
        _logger.LogDebug("Cleared conversion path cache");
    }

    /// <summary>
    /// Gets statistics about the registered converters
    /// </summary>
    public MessageConverterRegistryStatistics GetStatistics()
    {
        var totalConverters = _converters.Values.Sum(list => list.Count);
        var messageTypes = _converters.Keys.Count;
        var cachedPaths = _pathCache.Count;

        var convertersByType = _converters.ToDictionary(
            kvp => kvp.Key.Name,
            kvp => kvp.Value.Count
        );

        return new MessageConverterRegistryStatistics(
            totalConverters,
            messageTypes,
            cachedPaths,
            convertersByType
        );
    }

    /// <summary>
    /// Clears all registered converters and cached paths
    /// </summary>
    public void Clear()
    {
        _converters.Clear();
        _pathCache.Clear();
        _logger.LogInformation("Cleared all registered converters and cached paths");
    }

    private readonly record struct ConversionKey(Type MessageType, MessageVersion FromVersion, MessageVersion ToVersion);
}

/// <summary>
/// Statistics about the converter registry
/// </summary>
public class MessageConverterRegistryStatistics(
    int totalConverters,
    int messageTypes,
    int cachedPaths,
    IReadOnlyDictionary<string, int> convertersByType)
{
    public int TotalConverters { get; } = totalConverters;
    public int MessageTypes { get; } = messageTypes;
    public int CachedPaths { get; } = cachedPaths;
    public IReadOnlyDictionary<string, int> ConvertersByType { get; } = convertersByType;
}
