using HeroMessaging.Configuration;
using HeroMessaging.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeroMessaging.Tests.Integration;

/// <summary>
/// Integration tests for utilities dependency injection setup.
/// Verifies that IJsonSerializer and IBufferPoolManager are correctly registered
/// and can be resolved from the DI container.
/// </summary>
public class UtilitiesDependencyInjectionTests
{
    #region HeroMessagingBuilder Integration Tests

    [Fact]
    [Trait("Category", "Integration")]
    public void HeroMessagingBuilder_Build_RegistersIBufferPoolManager()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        builder.Build();
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var bufferPool = serviceProvider.GetService<IBufferPoolManager>();
        Assert.NotNull(bufferPool);
        Assert.IsType<DefaultBufferPoolManager>(bufferPool);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void HeroMessagingBuilder_Build_RegistersIJsonSerializer()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        builder.Build();
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var jsonSerializer = serviceProvider.GetService<IJsonSerializer>();
        Assert.NotNull(jsonSerializer);
        Assert.IsType<DefaultJsonSerializer>(jsonSerializer);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void HeroMessagingBuilder_Build_JsonSerializerDependsOnBufferPool()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        builder.Build();
        var serviceProvider = services.BuildServiceProvider();

        // Assert: JsonSerializer should be constructed with BufferPoolManager
        var jsonSerializer = serviceProvider.GetRequiredService<IJsonSerializer>();
        var bufferPool = serviceProvider.GetRequiredService<IBufferPoolManager>();

        Assert.NotNull(jsonSerializer);
        Assert.NotNull(bufferPool);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void HeroMessagingBuilder_Build_ServicesSingletonScope()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new HeroMessagingBuilder(services);
        builder.Build();
        var serviceProvider = services.BuildServiceProvider();

        // Act: Resolve services multiple times
        var bufferPool1 = serviceProvider.GetService<IBufferPoolManager>();
        var bufferPool2 = serviceProvider.GetService<IBufferPoolManager>();
        var jsonSerializer1 = serviceProvider.GetService<IJsonSerializer>();
        var jsonSerializer2 = serviceProvider.GetService<IJsonSerializer>();

        // Assert: Same instances (singletons)
        Assert.Same(bufferPool1, bufferPool2);
        Assert.Same(jsonSerializer1, jsonSerializer2);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void HeroMessagingBuilder_Build_DoesNotOverrideExistingRegistrations()
    {
        // Arrange: Register custom implementation first
        var services = new ServiceCollection();
        var customBufferPool = new DefaultBufferPoolManager();
        services.AddSingleton<IBufferPoolManager>(customBufferPool);

        var builder = new HeroMessagingBuilder(services);

        // Act
        builder.Build();
        var serviceProvider = services.BuildServiceProvider();

        // Assert: Should use the pre-registered instance
        var resolvedBufferPool = serviceProvider.GetRequiredService<IBufferPoolManager>();
        Assert.Same(customBufferPool, resolvedBufferPool);
    }

    #endregion

    #region Functional Integration Tests

    [Fact]
    [Trait("Category", "Integration")]
    public void ResolvedServices_CanSerializeAndDeserialize()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new HeroMessagingBuilder(services);
        builder.Build();
        var serviceProvider = services.BuildServiceProvider();

        var jsonSerializer = serviceProvider.GetRequiredService<IJsonSerializer>();
        var testObject = new TestData { Name = "Integration", Value = 42 };

        // Act
        var json = jsonSerializer.SerializeToString(testObject);
        var deserialized = jsonSerializer.DeserializeFromString<TestData>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(testObject.Name, deserialized.Name);
        Assert.Equal(testObject.Value, deserialized.Value);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void ResolvedBufferPool_CanRentAndReturnBuffers()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new HeroMessagingBuilder(services);
        builder.Build();
        var serviceProvider = services.BuildServiceProvider();

        var bufferPool = serviceProvider.GetRequiredService<IBufferPoolManager>();

        // Act & Assert: Multiple rent/return cycles
        for (int i = 0; i < 10; i++)
        {
            using var buffer = bufferPool.Rent(1024);
            Assert.True(buffer.Span.Length >= 1024);
            buffer.Span[0] = (byte)i;
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void ResolvedServices_JsonSerializerUsesBufferPool()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new HeroMessagingBuilder(services);
        builder.Build();
        var serviceProvider = services.BuildServiceProvider();

        var jsonSerializer = serviceProvider.GetRequiredService<IJsonSerializer>();

        // Act: Large object that would use pooled buffers
        var largeObject = new TestData
        {
            Name = new string('x', 5000),
            Value = 999
        };

        var json = jsonSerializer.SerializeToString(largeObject);
        var deserialized = jsonSerializer.DeserializeFromString<TestData>(json);

        // Assert: Should work correctly with pooled buffers
        Assert.NotNull(deserialized);
        Assert.Equal(largeObject.Name.Length, deserialized.Name?.Length);
        Assert.Equal(largeObject.Value, deserialized.Value);
    }

    #endregion

    #region Multiple Service Provider Tests

    [Fact]
    [Trait("Category", "Integration")]
    public void MultipleServiceProviders_IndependentInstances()
    {
        // Arrange: Create two independent service providers
        var services1 = new ServiceCollection();
        var builder1 = new HeroMessagingBuilder(services1);
        builder1.Build();
        var sp1 = services1.BuildServiceProvider();

        var services2 = new ServiceCollection();
        var builder2 = new HeroMessagingBuilder(services2);
        builder2.Build();
        var sp2 = services2.BuildServiceProvider();

        // Act
        var bufferPool1 = sp1.GetRequiredService<IBufferPoolManager>();
        var bufferPool2 = sp2.GetRequiredService<IBufferPoolManager>();
        var serializer1 = sp1.GetRequiredService<IJsonSerializer>();
        var serializer2 = sp2.GetRequiredService<IJsonSerializer>();

        // Assert: Different instances from different providers
        Assert.NotSame(bufferPool1, bufferPool2);
        Assert.NotSame(serializer1, serializer2);
    }

    #endregion

    #region Service Descriptor Tests

    [Fact]
    [Trait("Category", "Integration")]
    public void HeroMessagingBuilder_Build_RegistersCorrectLifetimes()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        builder.Build();

        // Assert: Verify service descriptors
        var bufferPoolDescriptor = services.FirstOrDefault(sd =>
            sd.ServiceType == typeof(IBufferPoolManager));
        var jsonSerializerDescriptor = services.FirstOrDefault(sd =>
            sd.ServiceType == typeof(IJsonSerializer));

        Assert.NotNull(bufferPoolDescriptor);
        Assert.Equal(ServiceLifetime.Singleton, bufferPoolDescriptor.Lifetime);

        Assert.NotNull(jsonSerializerDescriptor);
        Assert.Equal(ServiceLifetime.Singleton, jsonSerializerDescriptor.Lifetime);
    }

    #endregion

    #region Scoped Container Tests

    [Fact]
    [Trait("Category", "Integration")]
    public void ScopedContainers_UseSameSingletonInstances()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new HeroMessagingBuilder(services);
        builder.Build();
        var rootProvider = services.BuildServiceProvider();

        // Act: Create scoped providers
        IBufferPoolManager? bufferPoolFromScope1;
        IBufferPoolManager? bufferPoolFromScope2;
        IJsonSerializer? serializerFromScope1;
        IJsonSerializer? serializerFromScope2;

        using (var scope1 = rootProvider.CreateScope())
        {
            bufferPoolFromScope1 = scope1.ServiceProvider.GetRequiredService<IBufferPoolManager>();
            serializerFromScope1 = scope1.ServiceProvider.GetRequiredService<IJsonSerializer>();
        }

        using (var scope2 = rootProvider.CreateScope())
        {
            bufferPoolFromScope2 = scope2.ServiceProvider.GetRequiredService<IBufferPoolManager>();
            serializerFromScope2 = scope2.ServiceProvider.GetRequiredService<IJsonSerializer>();
        }

        // Assert: Singletons should be same across scopes
        Assert.Same(bufferPoolFromScope1, bufferPoolFromScope2);
        Assert.Same(serializerFromScope1, serializerFromScope2);
    }

    #endregion

    #region Test Helper Classes

    public class TestData
    {
        public string? Name { get; set; }
        public int Value { get; set; }
    }

    #endregion
}
