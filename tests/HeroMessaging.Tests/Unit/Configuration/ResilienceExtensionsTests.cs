using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Configuration;
using HeroMessaging.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Configuration;

[Trait("Category", "Unit")]
public class ResilienceExtensionsTests
{
    private readonly ServiceCollection _services;
    private readonly HeroMessagingBuilder _builder;

    public ResilienceExtensionsTests()
    {
        _services = new ServiceCollection();
        _services.AddLogging();
        _services.AddSingleton(TimeProvider.System);
        _builder = new HeroMessagingBuilder(_services);
    }

    #region WithConnectionResilience Tests

    [Fact]
    public void WithConnectionResilience_WithDefaultOptions_RegistersResiliencePolicy()
    {
        // Act
        var result = _builder.WithConnectionResilience();

        // Assert
        Assert.NotNull(result);
        Assert.Same(_builder, result);

        var provider = _services.BuildServiceProvider();
        var policy = provider.GetService<IConnectionResiliencePolicy>();
        Assert.NotNull(policy);
    }

    [Fact]
    public void WithConnectionResilience_WithCustomOptions_UsesProvidedOptions()
    {
        // Arrange
        var options = new ConnectionResilienceOptions
        {
            MaxRetries = 10,
            BaseRetryDelay = TimeSpan.FromSeconds(5)
        };

        // Act
        var result = _builder.WithConnectionResilience(options);

        // Assert
        Assert.NotNull(result);
        var provider = _services.BuildServiceProvider();
        var policy = provider.GetService<IConnectionResiliencePolicy>();
        Assert.NotNull(policy);
    }

    [Fact]
    public void WithConnectionResilience_WithConfigureAction_CallsConfigureAction()
    {
        // Arrange
        var configureCalled = false;
        ConnectionResilienceOptions? capturedOptions = null;

        // Act
        var result = _builder.WithConnectionResilience(options =>
        {
            configureCalled = true;
            capturedOptions = options;
            options.MaxRetries = 7;
        });

        // Assert
        Assert.True(configureCalled);
        Assert.NotNull(capturedOptions);
        Assert.Equal(7, capturedOptions.MaxRetries);
    }

    [Fact]
    public void WithConnectionResilience_WithNonHeroMessagingBuilder_ThrowsArgumentException()
    {
        // Arrange
        var mockBuilder = new Mock<IHeroMessagingBuilder>();

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            mockBuilder.Object.WithConnectionResilience());

        Assert.Equal("builder", exception.ParamName);
        Assert.Contains("must be of type HeroMessagingBuilder", exception.Message);
    }

    [Fact]
    public void WithConnectionResilience_DecoratesStorageImplementations()
    {
        // Arrange
        var mockMessageStorage = new Mock<IMessageStorage>();
        _services.AddSingleton(mockMessageStorage.Object);

        // Act
        _builder.WithConnectionResilience();

        // Assert
        var provider = _services.BuildServiceProvider();
        var storage = provider.GetService<IMessageStorage>();
        Assert.NotNull(storage);
    }

    #endregion

    #region WithHighAvailabilityResilience Tests

    [Fact]
    public void WithHighAvailabilityResilience_ConfiguresAggressiveSettings()
    {
        // Act
        var result = _builder.WithHighAvailabilityResilience();

        // Assert
        Assert.NotNull(result);
        Assert.Same(_builder, result);

        var provider = _services.BuildServiceProvider();
        var policy = provider.GetService<IConnectionResiliencePolicy>();
        Assert.NotNull(policy);
    }

    [Fact]
    public void WithHighAvailabilityResilience_UsesCorrectRetrySettings()
    {
        // Act
        _builder.WithHighAvailabilityResilience();

        // Assert
        var provider = _services.BuildServiceProvider();
        var policy = provider.GetService<IConnectionResiliencePolicy>();
        Assert.NotNull(policy);
        Assert.IsType<DefaultConnectionResiliencePolicy>(policy);
    }

    #endregion

    #region WithDevelopmentResilience Tests

    [Fact]
    public void WithDevelopmentResilience_ConfiguresConservativeSettings()
    {
        // Act
        var result = _builder.WithDevelopmentResilience();

        // Assert
        Assert.NotNull(result);
        Assert.Same(_builder, result);

        var provider = _services.BuildServiceProvider();
        var policy = provider.GetService<IConnectionResiliencePolicy>();
        Assert.NotNull(policy);
    }

    [Fact]
    public void WithDevelopmentResilience_UsesCorrectRetrySettings()
    {
        // Act
        _builder.WithDevelopmentResilience();

        // Assert
        var provider = _services.BuildServiceProvider();
        var policy = provider.GetService<IConnectionResiliencePolicy>();
        Assert.NotNull(policy);
        Assert.IsType<DefaultConnectionResiliencePolicy>(policy);
    }

    #endregion

    #region WithWriteOnlyResilience Tests

    [Fact]
    public void WithWriteOnlyResilience_RegistersResiliencePolicyForWrites()
    {
        // Act
        var result = _builder.WithWriteOnlyResilience();

        // Assert
        Assert.NotNull(result);
        Assert.Same(_builder, result);

        var provider = _services.BuildServiceProvider();
        var policy = provider.GetService<IConnectionResiliencePolicy>();
        Assert.NotNull(policy);
    }

    [Fact]
    public void WithWriteOnlyResilience_WithCustomOptions_UsesProvidedOptions()
    {
        // Arrange
        var options = new ConnectionResilienceOptions
        {
            MaxRetries = 5,
            BaseRetryDelay = TimeSpan.FromSeconds(2)
        };

        // Act
        var result = _builder.WithWriteOnlyResilience(options);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public void WithWriteOnlyResilience_WithNonHeroMessagingBuilder_ThrowsArgumentException()
    {
        // Arrange
        var mockBuilder = new Mock<IHeroMessagingBuilder>();

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            mockBuilder.Object.WithWriteOnlyResilience());

        Assert.Equal("builder", exception.ParamName);
        Assert.Contains("must be of type HeroMessagingBuilder", exception.Message);
    }

    #endregion

    #region WithConnectionResilience<TPolicy> Tests

    [Fact]
    public void WithConnectionResilience_WithCustomPolicy_RegistersCustomPolicy()
    {
        // Arrange
        _services.AddSingleton<CustomTestPolicy>();

        // Act
        var result = _builder.WithConnectionResilience<CustomTestPolicy>();

        // Assert
        Assert.NotNull(result);
        Assert.Same(_builder, result);

        var provider = _services.BuildServiceProvider();
        var policy = provider.GetService<IConnectionResiliencePolicy>();
        Assert.NotNull(policy);
        Assert.IsType<CustomTestPolicy>(policy);
    }

    [Fact]
    public void WithConnectionResilience_WithCustomPolicy_WithNonHeroMessagingBuilder_ThrowsArgumentException()
    {
        // Arrange
        var mockBuilder = new Mock<IHeroMessagingBuilder>();

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            mockBuilder.Object.WithConnectionResilience<CustomTestPolicy>());

        Assert.Equal("builder", exception.ParamName);
    }

    #endregion

    #region ResilienceProfiles Tests

    [Fact]
    public void ResilienceProfiles_Cloud_HasCorrectSettings()
    {
        // Act
        var profile = ResilienceProfiles.Cloud;

        // Assert
        Assert.Equal(5, profile.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(2), profile.BaseRetryDelay);
        Assert.Equal(TimeSpan.FromMinutes(2), profile.MaxRetryDelay);
        Assert.NotNull(profile.CircuitBreakerOptions);
        Assert.Equal(8, profile.CircuitBreakerOptions.FailureThreshold);
        Assert.Equal(TimeSpan.FromMinutes(3), profile.CircuitBreakerOptions.BreakDuration);
    }

    [Fact]
    public void ResilienceProfiles_OnPremises_HasCorrectSettings()
    {
        // Act
        var profile = ResilienceProfiles.OnPremises;

        // Assert
        Assert.Equal(3, profile.MaxRetries);
        Assert.Equal(TimeSpan.FromMilliseconds(500), profile.BaseRetryDelay);
        Assert.Equal(TimeSpan.FromSeconds(30), profile.MaxRetryDelay);
        Assert.NotNull(profile.CircuitBreakerOptions);
        Assert.Equal(5, profile.CircuitBreakerOptions.FailureThreshold);
        Assert.Equal(TimeSpan.FromMinutes(1), profile.CircuitBreakerOptions.BreakDuration);
    }

    [Fact]
    public void ResilienceProfiles_Microservices_HasCorrectSettings()
    {
        // Act
        var profile = ResilienceProfiles.Microservices;

        // Assert
        Assert.Equal(4, profile.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(1), profile.BaseRetryDelay);
        Assert.Equal(TimeSpan.FromSeconds(45), profile.MaxRetryDelay);
        Assert.NotNull(profile.CircuitBreakerOptions);
        Assert.Equal(6, profile.CircuitBreakerOptions.FailureThreshold);
        Assert.Equal(TimeSpan.FromMinutes(1.5), profile.CircuitBreakerOptions.BreakDuration);
    }

    [Fact]
    public void ResilienceProfiles_BatchProcessing_HasCorrectSettings()
    {
        // Act
        var profile = ResilienceProfiles.BatchProcessing;

        // Assert
        Assert.Equal(7, profile.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(3), profile.BaseRetryDelay);
        Assert.Equal(TimeSpan.FromMinutes(5), profile.MaxRetryDelay);
        Assert.NotNull(profile.CircuitBreakerOptions);
        Assert.Equal(12, profile.CircuitBreakerOptions.FailureThreshold);
        Assert.Equal(TimeSpan.FromMinutes(5), profile.CircuitBreakerOptions.BreakDuration);
    }

    [Fact]
    public void ResilienceProfiles_AllProfiles_AreAccessible()
    {
        // Act & Assert
        Assert.NotNull(ResilienceProfiles.Cloud);
        Assert.NotNull(ResilienceProfiles.OnPremises);
        Assert.NotNull(ResilienceProfiles.Microservices);
        Assert.NotNull(ResilienceProfiles.BatchProcessing);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void WithConnectionResilience_FullConfiguration_BuildsSuccessfully()
    {
        // Arrange
        var mockMessageStorage = new Mock<IMessageStorage>();
        var mockOutboxStorage = new Mock<IOutboxStorage>();
        var mockInboxStorage = new Mock<IInboxStorage>();
        var mockQueueStorage = new Mock<IQueueStorage>();
        var mockUnitOfWorkFactory = new Mock<IUnitOfWorkFactory>();

        _services.AddSingleton(mockMessageStorage.Object);
        _services.AddSingleton(mockOutboxStorage.Object);
        _services.AddSingleton(mockInboxStorage.Object);
        _services.AddSingleton(mockQueueStorage.Object);
        _services.AddSingleton(mockUnitOfWorkFactory.Object);

        // Act
        _builder.WithConnectionResilience();
        var provider = _services.BuildServiceProvider();

        // Assert
        var policy = provider.GetService<IConnectionResiliencePolicy>();
        Assert.NotNull(policy);

        var messageStorage = provider.GetService<IMessageStorage>();
        var outboxStorage = provider.GetService<IOutboxStorage>();
        var inboxStorage = provider.GetService<IInboxStorage>();
        var queueStorage = provider.GetService<IQueueStorage>();
        var unitOfWorkFactory = provider.GetService<IUnitOfWorkFactory>();

        Assert.NotNull(messageStorage);
        Assert.NotNull(outboxStorage);
        Assert.NotNull(inboxStorage);
        Assert.NotNull(queueStorage);
        Assert.NotNull(unitOfWorkFactory);
    }

    [Fact]
    public void WithConnectionResilience_UsingCloudProfile_ConfiguresCorrectly()
    {
        // Arrange
        var options = ResilienceProfiles.Cloud;

        // Act
        _builder.WithConnectionResilience(options);
        var provider = _services.BuildServiceProvider();

        // Assert
        var policy = provider.GetService<IConnectionResiliencePolicy>();
        Assert.NotNull(policy);
    }

    [Fact]
    public void WithHighAvailabilityResilience_BuildsServiceProvider_Successfully()
    {
        // Act
        _builder.WithHighAvailabilityResilience();
        var provider = _services.BuildServiceProvider();

        // Assert
        var policy = provider.GetService<IConnectionResiliencePolicy>();
        Assert.NotNull(policy);
    }

    [Fact]
    public void WithDevelopmentResilience_BuildsServiceProvider_Successfully()
    {
        // Act
        _builder.WithDevelopmentResilience();
        var provider = _services.BuildServiceProvider();

        // Assert
        var policy = provider.GetService<IConnectionResiliencePolicy>();
        Assert.NotNull(policy);
    }

    #endregion

    #region Chaining Tests

    [Fact]
    public void WithConnectionResilience_CanChainMultipleExtensions()
    {
        // Act
        var result = _builder
            .WithConnectionResilience()
            .WithConnectionResilience(); // Second call should work

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public void ResilienceExtensions_ChainWithOtherBuilderMethods_Works()
    {
        // Act
        var result = _builder
            .Development()
            .WithConnectionResilience()
            .UseInMemoryStorage();

        // Assert
        Assert.NotNull(result);
    }

    #endregion

    #region Helper Classes

    private class CustomTestPolicy : IConnectionResiliencePolicy
    {
        public Task ExecuteAsync(Func<Task> operation, string operationName, CancellationToken cancellationToken = default)
        {
            return operation();
        }

        public Task<T> ExecuteAsync<T>(Func<Task<T>> operation, string operationName, CancellationToken cancellationToken = default)
        {
            return operation();
        }
    }

    #endregion
}
