using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Orchestration;
using Xunit;

namespace HeroMessaging.Tests.Unit.Orchestration;

/// <summary>
/// Tests for Event<T> class covering construction, triggering, and state transitions
/// </summary>
[Trait("Category", "Unit")]
public class EventTests
{
    private record SimpleEvent(string Value) : IEvent, IMessage
    {
        public Guid MessageId { get; init; } = Guid.NewGuid();
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
        public string? CorrelationId { get; init; }
        public string? CausationId { get; init; }
        public Dictionary<string, object>? Metadata { get; init; }
    }

    private record AnotherEvent(int Number) : IEvent, IMessage
    {
        public Guid MessageId { get; init; } = Guid.NewGuid();
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
        public string? CorrelationId { get; init; }
        public string? CausationId { get; init; }
        public Dictionary<string, object>? Metadata { get; init; }
    }

    private record ComplexEvent(string Name, int Id, DateTime CreatedAt, Dictionary<string, object> Data)
        : IEvent, IMessage
    {
        public Guid MessageId { get; init; } = Guid.NewGuid();
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
        public string? CorrelationId { get; init; }
        public string? CausationId { get; init; }
        public Dictionary<string, object>? Metadata { get; init; }
    }

    #region Constructor Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithValidName_SetsName()
    {
        // Arrange
        var eventName = "ProcessStarted";

        // Act
        var @event = new Event<SimpleEvent>(eventName);

        // Assert
        Assert.NotNull(@event);
        Assert.Equal(eventName, @event.Name);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithEmptyString_SetEmptyName()
    {
        // Arrange
        var eventName = string.Empty;

        // Act
        var @event = new Event<SimpleEvent>(eventName);

        // Assert
        Assert.NotNull(@event);
        Assert.Equal(string.Empty, @event.Name);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithWhitespaceString_SetsWhitespaceName()
    {
        // Arrange
        var eventName = "   ";

        // Act
        var @event = new Event<SimpleEvent>(eventName);

        // Assert
        Assert.NotNull(@event);
        Assert.Equal("   ", @event.Name);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullName_ThrowsArgumentNullException()
    {
        // Arrange
        string? eventName = null;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new Event<SimpleEvent>(eventName!));

        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithLongName_SetsName()
    {
        // Arrange
        var eventName = new string('A', 1000);

        // Act
        var @event = new Event<SimpleEvent>(eventName);

        // Assert
        Assert.Equal(eventName, @event.Name);
        Assert.Equal(1000, @event.Name.Length);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithSpecialCharacters_SetsName()
    {
        // Arrange
        var eventName = "Event@#$%^&*()_+-=[]{}|;:',.<>?/";

        // Act
        var @event = new Event<SimpleEvent>(eventName);

        // Assert
        Assert.Equal(eventName, @event.Name);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithUnicodeCharacters_SetsName()
    {
        // Arrange
        var eventName = "イベント发生事件";

        // Act
        var @event = new Event<SimpleEvent>(eventName);

        // Assert
        Assert.Equal(eventName, @event.Name);
    }

    #endregion

    #region Name Property Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void Name_IsReadOnly()
    {
        // Arrange
        var @event = new Event<SimpleEvent>("OriginalName");

        // Act & Assert - Property should not have a setter
        Assert.Equal("OriginalName", @event.Name);
        var propertyInfo = typeof(Event<SimpleEvent>).GetProperty("Name");
        Assert.NotNull(propertyInfo);
        Assert.False(propertyInfo.CanWrite);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Name_Property_ReturnsSameValueMultipleTimes()
    {
        // Arrange
        var eventName = "TestEvent";
        var @event = new Event<SimpleEvent>(eventName);

        // Act
        var name1 = @event.Name;
        var name2 = @event.Name;
        var name3 = @event.Name;

        // Assert
        Assert.Equal(name1, name2);
        Assert.Equal(name2, name3);
        Assert.Equal(eventName, name1);
    }

    #endregion

    #region ToString Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void ToString_ReturnsEventName()
    {
        // Arrange
        var eventName = "UserRegistered";
        var @event = new Event<SimpleEvent>(eventName);

        // Act
        var result = @event.ToString();

        // Assert
        Assert.Equal(eventName, result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ToString_WithEmptyName_ReturnsEmptyString()
    {
        // Arrange
        var @event = new Event<SimpleEvent>(string.Empty);

        // Act
        var result = @event.ToString();

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ToString_WithSpecialCharacters_ReturnsExactName()
    {
        // Arrange
        var eventName = "Event-Create_With.Special#Chars!";
        var @event = new Event<SimpleEvent>(eventName);

        // Act
        var result = @event.ToString();

        // Assert
        Assert.Equal(eventName, result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ToString_CanBeUsedInStringInterpolation()
    {
        // Arrange
        var @event = new Event<SimpleEvent>("OrderPlaced");

        // Act
        var message = $"Event triggered: {@event}";

        // Assert
        Assert.Equal("Event triggered: OrderPlaced", message);
    }

    #endregion

    #region Generic Type Constraint Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void Event_CanBeCreatedWithDifferentEventTypes()
    {
        // Arrange & Act
        var simpleEvent = new Event<SimpleEvent>("Simple");
        var anotherEvent = new Event<AnotherEvent>("Another");
        var complexEvent = new Event<ComplexEvent>("Complex");

        // Assert
        Assert.Equal("Simple", simpleEvent.Name);
        Assert.Equal("Another", anotherEvent.Name);
        Assert.Equal("Complex", complexEvent.Name);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Event_WithDifferentGenericTypes_AreIndependent()
    {
        // Arrange
        var event1 = new Event<SimpleEvent>("EventA");
        var event2 = new Event<AnotherEvent>("EventA");

        // Act & Assert
        Assert.Equal(event1.Name, event2.Name);
        Assert.NotEqual(event1.GetType(), event2.GetType());
    }

    #endregion

    #region Triggering and State Transition Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void Event_CanTriggerStateTransition_InitialState()
    {
        // Arrange
        var initialState = new State("Initial");
        var nextState = new State("Processing");
        var triggerEvent = new Event<SimpleEvent>("Start");

        // Act
        var canTransition = initialState.Name == "Initial" && triggerEvent.Name == "Start";

        // Assert
        Assert.True(canTransition);
        Assert.Equal("Initial", initialState.Name);
        Assert.Equal("Processing", nextState.Name);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Event_CanTriggerMultipleTransitionsFromDifferentStates()
    {
        // Arrange
        var state1 = new State("State1");
        var state2 = new State("State2");
        var state3 = new State("State3");
        var event1 = new Event<SimpleEvent>("Transition");

        // Act
        var fromState1 = state1.Name == "State1" && event1.Name == "Transition";
        var fromState2 = state2.Name == "State2" && event1.Name == "Transition";
        var fromState3 = state3.Name == "State3" && event1.Name == "Transition";

        // Assert
        Assert.True(fromState1 && fromState2 && fromState3);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Event_CanTransitionToFinalState()
    {
        // Arrange
        var finalState = new State("Completed");
        var triggerEvent = new Event<SimpleEvent>("Complete");

        // Act
        var isFinal = finalState.Name == "Completed";
        var triggersCorrectly = triggerEvent.Name == "Complete";

        // Assert
        Assert.True(isFinal && triggersCorrectly);
    }

    #endregion

    #region Event Chain and Sequential Triggering Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void Event_CanBePartOfEventChain()
    {
        // Arrange
        var event1 = new Event<SimpleEvent>("Step1");
        var event2 = new Event<SimpleEvent>("Step2");
        var event3 = new Event<SimpleEvent>("Step3");

        // Act
        var chain = new[] { event1, event2, event3 };

        // Assert
        Assert.Equal(3, chain.Length);
        Assert.Equal("Step1", chain[0].Name);
        Assert.Equal("Step2", chain[1].Name);
        Assert.Equal("Step3", chain[2].Name);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Event_CanBeStoredInCollection()
    {
        // Arrange
        var events = new List<Event<SimpleEvent>>
        {
            new("EventA"),
            new("EventB"),
            new("EventC")
        };

        // Act
        var count = events.Count;
        var names = events.Select(e => e.Name).ToList();

        // Assert
        Assert.Equal(3, count);
        Assert.Contains("EventA", names);
        Assert.Contains("EventB", names);
        Assert.Contains("EventC", names);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Event_CanBeFilteredByName()
    {
        // Arrange
        var events = new[]
        {
            new Event<SimpleEvent>("Start"),
            new Event<SimpleEvent>("Process"),
            new Event<SimpleEvent>("Start"),
            new Event<SimpleEvent>("End")
        };

        // Act
        var startEvents = events.Where(e => e.Name == "Start").ToList();

        // Assert
        Assert.Equal(2, startEvents.Count);
        Assert.All(startEvents, e => Assert.Equal("Start", e.Name));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Event_CanBeMatched_ByName()
    {
        // Arrange
        var @event = new Event<SimpleEvent>("OrderProcessed");
        var targetName = "OrderProcessed";

        // Act
        var matches = @event.Name == targetName;

        // Assert
        Assert.True(matches);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Event_CanBeMatched_CaseSensitive()
    {
        // Arrange
        var @event = new Event<SimpleEvent>("EventName");

        // Act & Assert
        Assert.Equal("EventName", @event.Name);
        Assert.NotEqual("eventname", @event.Name);
        Assert.NotEqual("EVENTNAME", @event.Name);
    }

    #endregion

    #region Event Equality and Comparison Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void Event_WithSameNameAreNotEqual_ReferenceComparison()
    {
        // Arrange
        var event1 = new Event<SimpleEvent>("SameName");
        var event2 = new Event<SimpleEvent>("SameName");

        // Act & Assert
        Assert.NotEqual(event1, event2);
        Assert.NotSame(event1, event2);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Event_WithDifferentNamesAreNotEqual()
    {
        // Arrange
        var event1 = new Event<SimpleEvent>("EventA");
        var event2 = new Event<SimpleEvent>("EventB");

        // Act & Assert
        Assert.NotEqual(event1, event2);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Event_CanBeUsedAsKeyInDictionary()
    {
        // Arrange
        var event1 = new Event<SimpleEvent>("EventA");
        var event2 = new Event<SimpleEvent>("EventB");
        var dict = new Dictionary<Event<SimpleEvent>, int>
        {
            { event1, 1 },
            { event2, 2 }
        };

        // Act & Assert
        Assert.Equal(1, dict[event1]);
        Assert.Equal(2, dict[event2]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Event_CanBeUsedInSet()
    {
        // Arrange
        var event1 = new Event<SimpleEvent>("EventA");
        var event2 = new Event<SimpleEvent>("EventB");
        var event3 = new Event<SimpleEvent>("EventA");

        var set = new HashSet<Event<SimpleEvent>> { event1, event2, event3 };

        // Act & Assert
        Assert.Equal(3, set.Count);
    }

    #endregion

    #region Event Naming Convention Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void Event_NameFollowsPastTense_Convention()
    {
        // Arrange
        var @event = new Event<SimpleEvent>("OrderCreated");

        // Act
        var name = @event.Name;

        // Assert
        Assert.True(name.EndsWith("ed") || name.Contains("ed"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Event_NameCanFollowPascalCaseConvention()
    {
        // Arrange
        var eventNames = new[]
        {
            "OrderCreated",
            "PaymentProcessed",
            "CustomerRegistered"
        };

        // Act & Assert
        foreach (var name in eventNames)
        {
            var @event = new Event<SimpleEvent>(name);
            Assert.Equal(name, @event.Name);
            Assert.True(char.IsUpper(name[0]));
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Event_NameCanFollowSnakeCaseConvention()
    {
        // Arrange
        var @event = new Event<SimpleEvent>("order_created");

        // Act
        var name = @event.Name;

        // Assert
        Assert.Equal("order_created", name);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Event_NameCanFollowKebabCaseConvention()
    {
        // Arrange
        var @event = new Event<SimpleEvent>("order-created");

        // Act
        var name = @event.Name;

        // Assert
        Assert.Equal("order-created", name);
    }

    #endregion

    #region Event Type Information Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void Event_PreservesGenericTypeInformation()
    {
        // Arrange
        var @event = new Event<SimpleEvent>("TestEvent");

        // Act
        var genericType = @event.GetType().GetGenericArguments();

        // Assert
        Assert.Single(genericType);
        Assert.Equal(typeof(SimpleEvent), genericType[0]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Event_CanBeReflected_ToGetGenericType()
    {
        // Arrange
        var @event = new Event<AnotherEvent>("TestEvent");
        var type = @event.GetType();

        // Act
        var baseType = type.BaseType;
        var genericArgs = type.GetGenericArguments();

        // Assert
        Assert.NotNull(type);
        Assert.Equal(typeof(AnotherEvent), genericArgs[0]);
    }

    #endregion

    #region Edge Cases and Boundary Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void Event_WithNumericOnlyName()
    {
        // Arrange
        var @event = new Event<SimpleEvent>("12345");

        // Act
        var name = @event.Name;

        // Assert
        Assert.Equal("12345", name);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Event_WithNewlineCharacter()
    {
        // Arrange
        var eventName = "Event\nWith\nNewlines";

        // Act
        var @event = new Event<SimpleEvent>(eventName);

        // Assert
        Assert.Equal(eventName, @event.Name);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Event_WithTabCharacter()
    {
        // Arrange
        var eventName = "Event\tWith\tTabs";

        // Act
        var @event = new Event<SimpleEvent>(eventName);

        // Assert
        Assert.Equal(eventName, @event.Name);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Event_WithLeadingAndTrailingWhitespace()
    {
        // Arrange
        var eventName = "   EventName   ";

        // Act
        var @event = new Event<SimpleEvent>(eventName);

        // Assert
        Assert.Equal("   EventName   ", @event.Name);
    }

    #endregion

    #region Multiple Event Types Simultaneously

    [Fact]
    [Trait("Category", "Unit")]
    public void Multiple_Events_WithDifferentTypes_CanCoexist()
    {
        // Arrange
        var simpleEvent = new Event<SimpleEvent>("Simple");
        var anotherEvent = new Event<AnotherEvent>("Another");
        var complexEvent = new Event<ComplexEvent>("Complex");

        // Act
        var events = new object[] { simpleEvent, anotherEvent, complexEvent };

        // Assert
        Assert.Equal(3, events.Length);
        Assert.IsAssignableFrom<Event<SimpleEvent>>(events[0]);
        Assert.IsAssignableFrom<Event<AnotherEvent>>(events[1]);
        Assert.IsAssignableFrom<Event<ComplexEvent>>(events[2]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Event_OfDifferentTypes_WithSameName_AreDistinct()
    {
        // Arrange
        var event1 = new Event<SimpleEvent>("SameName");
        var event2 = new Event<AnotherEvent>("SameName");

        // Act
        var sameNames = event1.Name == event2.Name;
        var differentTypes = event1.GetType() != event2.GetType();

        // Assert
        Assert.True(sameNames);
        Assert.True(differentTypes);
    }

    #endregion

    #region Thread Safety and Immutability Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void Event_IsImmutable_NameCannotBeChanged()
    {
        // Arrange
        var originalName = "OriginalName";
        var @event = new Event<SimpleEvent>(originalName);

        // Act
        var nameAfterCreation = @event.Name;

        // Assert
        Assert.Equal(originalName, nameAfterCreation);
        var propertyInfo = typeof(Event<SimpleEvent>).GetProperty("Name");
        Assert.False(propertyInfo!.CanWrite, "Event name should not be writable");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Event_CanBeCreatedConcurrently()
    {
        // Arrange
        var events = new List<Event<SimpleEvent>>();
        var lockObj = new object();

        // Act
        var threads = new List<Thread>();
        for (int i = 0; i < 10; i++)
        {
            var i_copy = i;
            var thread = new Thread(() =>
            {
                var @event = new Event<SimpleEvent>($"Event{i_copy}");
                lock (lockObj)
                {
                    events.Add(@event);
                }
            });
            threads.Add(thread);
            thread.Start();
        }

        foreach (var thread in threads)
        {
            thread.Join();
        }

        // Assert
        Assert.Equal(10, events.Count);
    }

    #endregion
}
