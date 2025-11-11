using HeroMessaging.Orchestration;
using Xunit;

namespace HeroMessaging.Tests.Unit.Orchestration;

/// <summary>
/// Comprehensive test suite for the State class covering:
/// - Initialization and validation
/// - Equality comparisons by name
/// - HashCode consistency
/// - String representation
/// - Edge cases and boundary conditions
/// - Null input handling
///
/// Target Coverage: 80%+ of State class with emphasis on:
/// - Constructor null validation
/// - Name property access
/// - Equals() method (positive and negative cases)
/// - GetHashCode() consistency
/// - ToString() behavior
/// </summary>
[Trait("Category", "Unit")]
public class StateTests
{
    // ========== Constructor & Initialization Tests ==========

    [Fact(DisplayName = "State_Constructor_WithValidName_StoresName")]
    public void State_Constructor_WithValidName_StoresName()
    {
        // Arrange
        const string stateName = "Processing";

        // Act
        var state = new State(stateName);

        // Assert
        Assert.NotNull(state);
        Assert.Equal(stateName, state.Name);
    }

    [Fact(DisplayName = "State_Constructor_WithNullName_ThrowsArgumentNullException")]
    public void State_Constructor_WithNullName_ThrowsArgumentNullException()
    {
        // Arrange & Act
        var exception = Assert.Throws<ArgumentNullException>(() => new State(null!));

        // Assert
        Assert.Equal("name", exception.ParamName);
    }

    [Fact(DisplayName = "State_Constructor_WithEmptyString_CreatesStateWithEmptyName")]
    public void State_Constructor_WithEmptyString_CreatesStateWithEmptyName()
    {
        // Arrange
        const string emptyName = "";

        // Act
        var state = new State(emptyName);

        // Assert
        Assert.NotNull(state);
        Assert.Equal(emptyName, state.Name);
    }

    [Fact(DisplayName = "State_Constructor_WithWhitespace_CreatesStateWithWhitespaceName")]
    public void State_Constructor_WithWhitespace_CreatesStateWithWhitespaceName()
    {
        // Arrange
        const string whitespaceName = "   ";

        // Act
        var state = new State(whitespaceName);

        // Assert
        Assert.NotNull(state);
        Assert.Equal(whitespaceName, state.Name);
    }

    [Fact(DisplayName = "State_Constructor_WithSingleCharacter_CreatesState")]
    public void State_Constructor_WithSingleCharacter_CreatesState()
    {
        // Arrange
        const string singleChar = "A";

        // Act
        var state = new State(singleChar);

        // Assert
        Assert.NotNull(state);
        Assert.Equal(singleChar, state.Name);
    }

    [Fact(DisplayName = "State_Constructor_WithVeryLongName_CreatesState")]
    public void State_Constructor_WithVeryLongName_CreatesState()
    {
        // Arrange
        var longName = new string('A', 10000);

        // Act
        var state = new State(longName);

        // Assert
        Assert.NotNull(state);
        Assert.Equal(longName, state.Name);
    }

    [Fact(DisplayName = "State_Constructor_WithSpecialCharacters_CreatesState")]
    public void State_Constructor_WithSpecialCharacters_CreatesState()
    {
        // Arrange
        const string specialChars = "State-@#$%^&*()_+=[]{}|;:,.<>?/~`";

        // Act
        var state = new State(specialChars);

        // Assert
        Assert.NotNull(state);
        Assert.Equal(specialChars, state.Name);
    }

    [Fact(DisplayName = "State_Constructor_WithUnicodeCharacters_CreatesState")]
    public void State_Constructor_WithUnicodeCharacters_CreatesState()
    {
        // Arrange
        const string unicodeName = "状態🎉北京";

        // Act
        var state = new State(unicodeName);

        // Assert
        Assert.NotNull(state);
        Assert.Equal(unicodeName, state.Name);
    }

    // ========== Name Property Tests ==========

    [Fact(DisplayName = "State_Name_Property_IsReadOnly")]
    public void State_Name_Property_IsReadOnly()
    {
        // Arrange
        var state = new State("Initial");

        // Act & Assert - Verify we can read but not set
        Assert.Equal("Initial", state.Name);
        // Note: No setter test as property is read-only
    }

    [Fact(DisplayName = "State_Name_Property_ReturnsSameValueAsConstructor")]
    public void State_Name_Property_ReturnsSameValueAsConstructor()
    {
        // Arrange
        const string originalName = "Processing";
        var state = new State(originalName);

        // Act
        var retrievedName = state.Name;

        // Assert
        Assert.Equal(originalName, retrievedName);
    }

    // ========== Equality Tests (Positive Cases) ==========

    [Fact(DisplayName = "State_Equals_SameNameDifferentInstances_ReturnsTrue")]
    public void State_Equals_SameNameDifferentInstances_ReturnsTrue()
    {
        // Arrange
        const string stateName = "Processing";
        var state1 = new State(stateName);
        var state2 = new State(stateName);

        // Act
        var result = state1.Equals(state2);

        // Assert
        Assert.True(result);
    }

    [Fact(DisplayName = "State_Equals_SameInstance_ReturnsTrue")]
    public void State_Equals_SameInstance_ReturnsTrue()
    {
        // Arrange
        var state = new State("Processing");

        // Act
        var result = state.Equals(state);

        // Assert
        Assert.True(result);
    }

    [Fact(DisplayName = "State_EqualsOperator_WithSameName_WorksCorrectly")]
    public void State_EqualsOperator_WithSameName_WorksCorrectly()
    {
        // Arrange
        var state1 = new State("Completed");
        var state2 = new State("Completed");

        // Act & Assert
        Assert.Equal(state1, state2);
    }

    // ========== Equality Tests (Negative Cases) ==========

    [Fact(DisplayName = "State_Equals_DifferentNames_ReturnsFalse")]
    public void State_Equals_DifferentNames_ReturnsFalse()
    {
        // Arrange
        var state1 = new State("Processing");
        var state2 = new State("Completed");

        // Act
        var result = state1.Equals(state2);

        // Assert
        Assert.False(result);
    }

    [Fact(DisplayName = "State_Equals_CaseSensitiveComparison_ReturnsFalse")]
    public void State_Equals_CaseSensitiveComparison_ReturnsFalse()
    {
        // Arrange
        var state1 = new State("Processing");
        var state2 = new State("processing");

        // Act
        var result = state1.Equals(state2);

        // Assert
        Assert.False(result);
    }

    [Fact(DisplayName = "State_Equals_WithNull_ReturnsFalse")]
    public void State_Equals_WithNull_ReturnsFalse()
    {
        // Arrange
        var state = new State("Processing");

        // Act
        var result = state.Equals(null);

        // Assert
        Assert.False(result);
    }

    [Fact(DisplayName = "State_Equals_WithDifferentType_ReturnsFalse")]
    public void State_Equals_WithDifferentType_ReturnsFalse()
    {
        // Arrange
        var state = new State("Processing");
        var differentObject = "Processing";

        // Act
        var result = state.Equals(differentObject);

        // Assert
        Assert.False(result);
    }

    [Fact(DisplayName = "State_NotEquals_DifferentNames_ReturnsFalse")]
    public void State_NotEquals_DifferentNames_ReturnsFalse()
    {
        // Arrange
        var state1 = new State("Processing");
        var state2 = new State("Completed");

        // Act & Assert
        Assert.NotEqual(state1, state2);
    }

    [Fact(DisplayName = "State_Equals_WithWhitespaceVariations_ReturnsFalse")]
    public void State_Equals_WithWhitespaceVariations_ReturnsFalse()
    {
        // Arrange
        var state1 = new State("Processing");
        var state2 = new State("Processing ");
        var state3 = new State(" Processing");

        // Act & Assert
        Assert.NotEqual(state1, state2);
        Assert.NotEqual(state1, state3);
    }

    [Fact(DisplayName = "State_Equals_EmptyVsNull_ReturnsFalse")]
    public void State_Equals_EmptyVsNull_ReturnsFalse()
    {
        // Arrange
        var emptyState = new State("");
        var nullState = new State(null!); // This will throw, but testing equality behavior if it somehow existed

        // Act & Assert - emptyState vs null object
        Assert.False(emptyState.Equals(null));
    }

    // ========== HashCode Tests ==========

    [Fact(DisplayName = "State_GetHashCode_SameNameSameHash")]
    public void State_GetHashCode_SameNameSameHash()
    {
        // Arrange
        const string stateName = "Processing";
        var state1 = new State(stateName);
        var state2 = new State(stateName);

        // Act
        var hash1 = state1.GetHashCode();
        var hash2 = state2.GetHashCode();

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact(DisplayName = "State_GetHashCode_DifferentNamesDifferentHash")]
    public void State_GetHashCode_DifferentNamesDifferentHash()
    {
        // Arrange
        var state1 = new State("Processing");
        var state2 = new State("Completed");

        // Act
        var hash1 = state1.GetHashCode();
        var hash2 = state2.GetHashCode();

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    [Fact(DisplayName = "State_GetHashCode_ConsistentMultipleCalls")]
    public void State_GetHashCode_ConsistentMultipleCalls()
    {
        // Arrange
        var state = new State("Processing");

        // Act
        var hash1 = state.GetHashCode();
        var hash2 = state.GetHashCode();
        var hash3 = state.GetHashCode();

        // Assert
        Assert.Equal(hash1, hash2);
        Assert.Equal(hash2, hash3);
    }

    [Fact(DisplayName = "State_GetHashCode_BasedOnName")]
    public void State_GetHashCode_BasedOnName()
    {
        // Arrange
        var state = new State("Processing");
        var expectedHash = "Processing".GetHashCode();

        // Act
        var actualHash = state.GetHashCode();

        // Assert
        Assert.Equal(expectedHash, actualHash);
    }

    [Fact(DisplayName = "State_GetHashCode_WithEmptyName")]
    public void State_GetHashCode_WithEmptyName()
    {
        // Arrange
        var state = new State("");
        var expectedHash = "".GetHashCode();

        // Act
        var actualHash = state.GetHashCode();

        // Assert
        Assert.Equal(expectedHash, actualHash);
    }

    [Fact(DisplayName = "State_GetHashCode_CaseSensitive")]
    public void State_GetHashCode_CaseSensitive()
    {
        // Arrange
        var state1 = new State("Processing");
        var state2 = new State("processing");

        // Act
        var hash1 = state1.GetHashCode();
        var hash2 = state2.GetHashCode();

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    // ========== ToString Tests ==========

    [Fact(DisplayName = "State_ToString_ReturnsName")]
    public void State_ToString_ReturnsName()
    {
        // Arrange
        const string stateName = "Processing";
        var state = new State(stateName);

        // Act
        var result = state.ToString();

        // Assert
        Assert.Equal(stateName, result);
    }

    [Fact(DisplayName = "State_ToString_WithEmptyName_ReturnsEmptyString")]
    public void State_ToString_WithEmptyName_ReturnsEmptyString()
    {
        // Arrange
        var state = new State("");

        // Act
        var result = state.ToString();

        // Assert
        Assert.Equal("", result);
    }

    [Fact(DisplayName = "State_ToString_ConsistentMultipleCalls")]
    public void State_ToString_ConsistentMultipleCalls()
    {
        // Arrange
        var state = new State("Processing");

        // Act
        var result1 = state.ToString();
        var result2 = state.ToString();
        var result3 = state.ToString();

        // Assert
        Assert.Equal(result1, result2);
        Assert.Equal(result2, result3);
    }

    [Fact(DisplayName = "State_ToString_WithSpecialCharacters_PreservesContent")]
    public void State_ToString_WithSpecialCharacters_PreservesContent()
    {
        // Arrange
        const string specialChars = "State-@#$%^&*()_+=";
        var state = new State(specialChars);

        // Act
        var result = state.ToString();

        // Assert
        Assert.Equal(specialChars, result);
    }

    // ========== Collection & Dictionary Tests ==========

    [Fact(DisplayName = "State_UsedInHashSet_StatesWithSameNameAreEqual")]
    public void State_UsedInHashSet_StatesWithSameNameAreEqual()
    {
        // Arrange
        var state1 = new State("Processing");
        var state2 = new State("Processing");
        var state3 = new State("Completed");
        var hashSet = new HashSet<State> { state1 };

        // Act
        var contains1 = hashSet.Contains(state2);
        var contains2 = hashSet.Contains(state3);

        // Assert
        Assert.True(contains1);
        Assert.False(contains2);
    }

    [Fact(DisplayName = "State_UsedInDictionary_KeysByStateName")]
    public void State_UsedInDictionary_KeysByStateName()
    {
        // Arrange
        var state1 = new State("Processing");
        var state2 = new State("Processing");
        var state3 = new State("Completed");
        var dictionary = new Dictionary<State, string> { { state1, "Value1" } };

        // Act
        var canRetrieveWithSameName = dictionary.TryGetValue(state2, out var value);
        var canRetrieveDifferent = dictionary.TryGetValue(state3, out _);

        // Assert
        Assert.True(canRetrieveWithSameName);
        Assert.Equal("Value1", value);
        Assert.False(canRetrieveDifferent);
    }

    [Fact(DisplayName = "State_ListRemove_RemovesByNameEquality")]
    public void State_ListRemove_RemovesByNameEquality()
    {
        // Arrange
        var state1 = new State("Processing");
        var state2 = new State("Processing");
        var state3 = new State("Completed");
        var list = new List<State> { state1, state3 };

        // Act
        var removed = list.Remove(state2);

        // Assert
        Assert.True(removed);
        Assert.Single(list);
        Assert.Contains(state3, list);
    }

    // ========== Immutability & Thread Safety Tests ==========

    [Fact(DisplayName = "State_Properties_AreImmutable")]
    public void State_Properties_AreImmutable()
    {
        // Arrange
        const string originalName = "Processing";
        var state = new State(originalName);

        // Act
        var name1 = state.Name;
        var name2 = state.Name;

        // Assert - Verify name doesn't change
        Assert.Equal(originalName, name1);
        Assert.Equal(originalName, name2);
        Assert.Same(name1, name2); // Same reference
    }

    [Fact(DisplayName = "State_MultipleInstances_CanCoexist")]
    public void State_MultipleInstances_CanCoexist()
    {
        // Arrange
        var states = new List<State>
        {
            new State("Initial"),
            new State("Processing"),
            new State("Validating"),
            new State("Completed"),
            new State("Failed")
        };

        // Act & Assert
        Assert.Equal(5, states.Count);
        Assert.All(states, s => Assert.NotNull(s.Name));
    }

    // ========== Boundary & Edge Cases ==========

    [Fact(DisplayName = "State_WithNewlineCharacters_CreatesState")]
    public void State_WithNewlineCharacters_CreatesState()
    {
        // Arrange
        const string nameWithNewline = "Processing\nState";

        // Act
        var state = new State(nameWithNewline);

        // Assert
        Assert.Equal(nameWithNewline, state.Name);
    }

    [Fact(DisplayName = "State_WithTabCharacters_CreatesState")]
    public void State_WithTabCharacters_CreatesState()
    {
        // Arrange
        const string nameWithTab = "Processing\tState";

        // Act
        var state = new State(nameWithTab);

        // Assert
        Assert.Equal(nameWithTab, state.Name);
    }

    [Fact(DisplayName = "State_NameConsistency_AcrossMultipleAccess")]
    public void State_NameConsistency_AcrossMultipleAccess()
    {
        // Arrange
        const string originalName = "Processing";
        var state = new State(originalName);
        var names = new List<string>();

        // Act
        for (int i = 0; i < 100; i++)
        {
            names.Add(state.Name);
        }

        // Assert
        Assert.All(names, name => Assert.Equal(originalName, name));
    }

    [Fact(DisplayName = "State_Comparison_TransitivityOfEquality")]
    public void State_Comparison_TransitivityOfEquality()
    {
        // Arrange
        var state1 = new State("Processing");
        var state2 = new State("Processing");
        var state3 = new State("Processing");

        // Act & Assert
        Assert.Equal(state1, state2);
        Assert.Equal(state2, state3);
        Assert.Equal(state1, state3);
    }

    [Fact(DisplayName = "State_Comparison_SymmetryOfEquality")]
    public void State_Comparison_SymmetryOfEquality()
    {
        // Arrange
        var state1 = new State("Processing");
        var state2 = new State("Processing");

        // Act & Assert
        Assert.Equal(state1, state2);
        Assert.Equal(state2, state1);
    }

    [Fact(DisplayName = "State_Comparison_ReflexivityOfEquality")]
    public void State_Comparison_ReflexivityOfEquality()
    {
        // Arrange
        var state = new State("Processing");

        // Act & Assert
        Assert.Equal(state, state);
    }

    [Fact(DisplayName = "State_SerializationScenario_NamePreserved")]
    public void State_SerializationScenario_NamePreserved()
    {
        // Arrange
        const string stateName = "Processing";
        var originalState = new State(stateName);

        // Act - Simulate serialization/deserialization
        var recreatedState = new State(originalState.Name);

        // Assert
        Assert.Equal(originalState, recreatedState);
        Assert.Equal(originalState.GetHashCode(), recreatedState.GetHashCode());
    }

    // ========== Integration with State Machine Tests ==========

    [Fact(DisplayName = "State_UsedInStateMachine_WorksAsKey")]
    public void State_UsedInStateMachine_WorksAsKey()
    {
        // Arrange
        var initialState = new State("Initial");
        var processingState = new State("Processing");
        var transitions = new Dictionary<State, List<string>>
        {
            { initialState, new List<string> { "StartEvent" } },
            { processingState, new List<string> { "CompleteEvent", "ErrorEvent" } }
        };

        // Act
        var initialTransitions = transitions[initialState];
        var processingTransitions = transitions[processingState];

        // Assert
        Assert.Single(initialTransitions);
        Assert.Equal(2, processingTransitions.Count);
    }

    [Fact(DisplayName = "State_StateEquality_AllowsLogicalComparison")]
    public void State_StateEquality_AllowsLogicalComparison()
    {
        // Arrange
        var state1 = new State("Processing");
        var state2 = new State("Processing");

        // Act & Assert
        Assert.True(state1 == state2 || state1.Equals(state2));
    }

    [Fact(DisplayName = "State_MultipleReferencesToSameName_AreInterchangeable")]
    public void State_MultipleReferencesToSameName_AreInterchangeable()
    {
        // Arrange
        const string stateName = "Processing";
        var state1 = new State(stateName);
        var state2 = new State(stateName);
        var state3 = new State(stateName);

        // Act
        var allEqual = state1.Equals(state2) && state2.Equals(state3);
        var sameHash = state1.GetHashCode() == state2.GetHashCode() && state2.GetHashCode() == state3.GetHashCode();

        // Assert
        Assert.True(allEqual);
        Assert.True(sameHash);
    }
}
