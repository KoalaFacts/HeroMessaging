using System.Text.Json;
using HeroMessaging.Abstractions.Storage;

namespace HeroMessaging.Abstractions.Tests.Storage;

[Trait("Category", "Unit")]
public class DefaultJsonOptionsProviderTests
{
    [Fact]
    public void GetOptions_ReturnsJsonSerializerOptions()
    {
        // Arrange
        var provider = new DefaultJsonOptionsProvider();

        // Act
        var options = provider.GetOptions();

        // Assert
        Assert.NotNull(options);
        Assert.IsType<JsonSerializerOptions>(options);
    }

    [Fact]
    public void GetOptions_ReturnsSameInstanceOnMultipleCalls()
    {
        // Arrange
        var provider = new DefaultJsonOptionsProvider();

        // Act
        var options1 = provider.GetOptions();
        var options2 = provider.GetOptions();

        // Assert
        Assert.Same(options1, options2);
    }

    [Fact]
    public void GetOptions_HasPropertyNameCaseInsensitive()
    {
        // Arrange
        var provider = new DefaultJsonOptionsProvider();

        // Act
        var options = provider.GetOptions();

        // Assert
        Assert.True(options.PropertyNameCaseInsensitive);
    }

    [Fact]
    public void GetOptions_HasWriteIndentedFalse()
    {
        // Arrange
        var provider = new DefaultJsonOptionsProvider();

        // Act
        var options = provider.GetOptions();

        // Assert
        Assert.False(options.WriteIndented);
    }

    [Fact]
    public void ImplementsIJsonOptionsProvider()
    {
        // Arrange & Act
        var provider = new DefaultJsonOptionsProvider();

        // Assert
        Assert.IsAssignableFrom<IJsonOptionsProvider>(provider);
    }

    [Fact]
    public void CanSerializeObject()
    {
        // Arrange
        var provider = new DefaultJsonOptionsProvider();
        var options = provider.GetOptions();
        var testObject = new { Name = "Test", Value = 123 };

        // Act
        var json = JsonSerializer.Serialize(testObject, options);

        // Assert
        Assert.NotNull(json);
        Assert.Contains("Test", json);
        Assert.Contains("123", json);
    }

    [Fact]
    public void CanDeserializeObject()
    {
        // Arrange
        var provider = new DefaultJsonOptionsProvider();
        var options = provider.GetOptions();
        var json = "{\"name\":\"Test\",\"value\":123}";

        // Act
        var result = JsonSerializer.Deserialize<TestClass>(json, options);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Test", result.Name);
        Assert.Equal(123, result.Value);
    }

    [Fact]
    public void CaseInsensitiveDeserialization_Works()
    {
        // Arrange
        var provider = new DefaultJsonOptionsProvider();
        var options = provider.GetOptions();
        var json = "{\"NaMe\":\"Test\",\"VaLuE\":123}";

        // Act
        var result = JsonSerializer.Deserialize<TestClass>(json, options);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Test", result.Name);
        Assert.Equal(123, result.Value);
    }

    [Fact]
    public void Serialization_IsNotIndented()
    {
        // Arrange
        var provider = new DefaultJsonOptionsProvider();
        var options = provider.GetOptions();
        var testObject = new { Name = "Test", Value = 123 };

        // Act
        var json = JsonSerializer.Serialize(testObject, options);

        // Assert
        Assert.DoesNotContain("\n", json);
        Assert.DoesNotContain("\r", json);
        Assert.DoesNotContain("  ", json);
    }

    [Fact]
    public void MultipleProviderInstances_UseSameOptions()
    {
        // Arrange
        var provider1 = new DefaultJsonOptionsProvider();
        var provider2 = new DefaultJsonOptionsProvider();

        // Act
        var options1 = provider1.GetOptions();
        var options2 = provider2.GetOptions();

        // Assert
        Assert.Same(options1, options2);
    }

    [Fact]
    public void OptionsAreThreadSafe()
    {
        // Arrange
        var provider = new DefaultJsonOptionsProvider();
        var tasks = new List<Task<JsonSerializerOptions>>();

        // Act
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() => provider.GetOptions()));
        }
        Task.WaitAll(tasks.ToArray());

        // Assert
        var firstOptions = tasks[0].Result;
        Assert.All(tasks, t => Assert.Same(firstOptions, t.Result));
    }

    private class TestClass
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }
}
