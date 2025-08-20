using System;
using System.Linq;
using System.Reflection;
using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.ErrorHandling;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Abstractions.Plugins;
using HeroMessaging.Abstractions.Queries;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Core;
using HeroMessaging.Core.Configuration;
using HeroMessaging.Core.ErrorHandling;
using HeroMessaging.Core.Processing;
using HeroMessaging.Core.Storage;
using HeroMessaging.Tests.Unit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Core.Configuration;

public class HeroMessagingBuilderTests
{
    private readonly ServiceCollection _services;
    private readonly HeroMessagingBuilder _sut;

    public HeroMessagingBuilderTests()
    {
        _services = new ServiceCollection();
        _services.AddLogging(); // Add logging for dependencies
        _sut = new HeroMessagingBuilder(_services);
    }

    [Fact]
    public void WithMediator_Should_Register_Command_And_Query_Processors()
    {
        // Act
        _sut.WithMediator().Build();

        // Assert
        var provider = _services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<ICommandProcessor>());
        Assert.NotNull(provider.GetService<IQueryProcessor>());
    }

    [Fact]
    public void WithEventBus_Should_Register_EventBus()
    {
        // Act
        _sut.WithEventBus().Build();

        // Assert
        var provider = _services.BuildServiceProvider();
        var eventBus = provider.GetService<IEventBus>();
        Assert.NotNull(eventBus);
        Assert.IsType<EventBusV2>(eventBus);
    }

    [Fact]
    public void WithQueues_Should_Register_QueueProcessor()
    {
        // Arrange
        _sut.UseInMemoryStorage(); // Required for queue processor

        // Act
        _sut.WithQueues().Build();

        // Assert
        var provider = _services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<IQueueProcessor>());
    }

    [Fact]
    public void WithOutbox_Should_Register_OutboxProcessor()
    {
        // Arrange
        _sut.UseInMemoryStorage(); // Required for outbox processor

        // Act
        _sut.WithOutbox().Build();

        // Assert
        var provider = _services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<IOutboxProcessor>());
    }

    [Fact]
    public void WithInbox_Should_Register_InboxProcessor()
    {
        // Arrange
        _sut.UseInMemoryStorage(); // Required for inbox processor

        // Act
        _sut.WithInbox().Build();

        // Assert
        var provider = _services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<IInboxProcessor>());
    }

    [Fact]
    public void UseInMemoryStorage_Should_Register_All_Storage_Implementations()
    {
        // Act
        _sut.UseInMemoryStorage().Build();

        // Assert
        var provider = _services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<IMessageStorage>());
        Assert.NotNull(provider.GetService<IOutboxStorage>());
        Assert.NotNull(provider.GetService<IInboxStorage>());
        Assert.NotNull(provider.GetService<IQueueStorage>());
    }

    [Fact]
    public void WithErrorHandling_Should_Register_Error_Components()
    {
        // Act
        _sut.WithErrorHandling().Build();

        // Assert
        var provider = _services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<IDeadLetterQueue>());
        Assert.NotNull(provider.GetService<IErrorHandler>());
    }

    [Fact]
    public void Development_Should_Configure_Development_Setup()
    {
        // Act
        _sut.Development().Build();

        // Assert
        var provider = _services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<ICommandProcessor>());
        Assert.NotNull(provider.GetService<IQueryProcessor>());
        Assert.NotNull(provider.GetService<IEventBus>());
        Assert.NotNull(provider.GetService<IMessageStorage>());
    }

    [Fact]
    public void Production_Should_Configure_All_Features()
    {
        // Arrange
        _sut.UseInMemoryStorage(); // Add storage for production features

        // Act
        _sut.Production("connection-string").Build();

        // Assert
        var provider = _services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<ICommandProcessor>());
        Assert.NotNull(provider.GetService<IQueryProcessor>());
        Assert.NotNull(provider.GetService<IEventBus>());
        Assert.NotNull(provider.GetService<IQueueProcessor>());
        Assert.NotNull(provider.GetService<IOutboxProcessor>());
        Assert.NotNull(provider.GetService<IInboxProcessor>());
    }

    [Fact]
    public void Microservice_Should_Configure_Microservice_Setup()
    {
        // Arrange
        _sut.UseInMemoryStorage(); // Add storage for microservice features

        // Act
        _sut.Microservice("connection-string").Build();

        // Assert
        var provider = _services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<IEventBus>());
        Assert.NotNull(provider.GetService<IOutboxProcessor>());
        Assert.NotNull(provider.GetService<IInboxProcessor>());
        
        // Should not have mediator components
        Assert.Null(provider.GetService<ICommandProcessor>());
        Assert.Null(provider.GetService<IQueryProcessor>());
    }

    [Fact]
    public void ConfigureProcessing_Should_Apply_Processing_Options()
    {
        // Act
        _sut.ConfigureProcessing(options =>
        {
            options.MaxConcurrency = 10;
            options.MaxRetries = 5;
            options.EnableCircuitBreaker = false;
        }).Build();

        // Assert
        var provider = _services.BuildServiceProvider();
        var options = provider.GetService<ProcessingOptions>();
        Assert.NotNull(options);
        Assert.Equal(10, options.MaxConcurrency);
        Assert.Equal(5, options.MaxRetries);
        Assert.False(options.EnableCircuitBreaker);
    }

    [Fact]
    public void ScanAssembly_Should_Register_Handlers()
    {
        // Act
        _sut.ScanAssembly(typeof(TestCommandHandler).Assembly).Build();

        // Assert
        var provider = _services.BuildServiceProvider();
        var handler = provider.GetService<ICommandHandler<TestCommand>>();
        Assert.NotNull(handler);
        // Handler could be either TestCommandHandler or MultipleInterfaceHandler
        Assert.IsAssignableFrom<ICommandHandler<TestCommand>>(handler);
    }

    [Fact]
    public void ScanAssemblies_Should_Register_Multiple_Assembly_Handlers()
    {
        // Act
        _sut.ScanAssemblies(
            typeof(TestCommandHandler).Assembly,
            typeof(TestCommandHandler).Assembly // Same assembly for test
        ).Build();

        // Assert
        var provider = _services.BuildServiceProvider();
        var commandHandler = provider.GetService<ICommandHandler<TestCommand>>();
        var queryHandler = provider.GetService<IQueryHandler<TestQuery, string>>();
        var eventHandler = provider.GetService<IEventHandler<TestEvent>>();
        
        Assert.NotNull(commandHandler);
        Assert.NotNull(queryHandler);
        Assert.NotNull(eventHandler);
    }

    [Fact]
    public void AddPlugin_Should_Register_Plugin()
    {
        // Arrange
        var pluginMock = new Mock<IMessagingPlugin>();

        // Act
        _sut.AddPlugin(pluginMock.Object).Build();

        // Assert
        pluginMock.Verify(x => x.Configure(It.IsAny<IServiceCollection>()), Times.Once);
        var provider = _services.BuildServiceProvider();
        var plugin = provider.GetService<IMessagingPlugin>();
        Assert.Same(pluginMock.Object, plugin);
    }

    [Fact]
    public void Build_Should_Register_HeroMessagingService()
    {
        // Act
        _sut.WithMediator().WithEventBus().Build();

        // Assert
        var provider = _services.BuildServiceProvider();
        var service = provider.GetService<IHeroMessaging>();
        Assert.NotNull(service);
        Assert.IsType<HeroMessagingService>(service);
    }

    [Fact]
    public void Build_Should_Register_All_Handler_Interfaces_From_Single_Class()
    {
        // Act
        _sut.ScanAssembly(typeof(MultipleInterfaceHandler).Assembly).Build();

        // Assert
        var provider = _services.BuildServiceProvider();
        
        // Should register for both command interfaces
        var commandHandler = provider.GetService<ICommandHandler<TestCommand>>();
        var commandWithResponseHandler = provider.GetService<ICommandHandler<TestCommandWithResponse, string>>();
        
        Assert.NotNull(commandHandler);
        Assert.NotNull(commandWithResponseHandler);
        Assert.IsType<MultipleInterfaceHandler>(commandHandler);
        Assert.IsType<MultipleInterfaceHandler>(commandWithResponseHandler);
    }

    [Fact]
    public void UseStorage_Generic_Should_Register_Custom_Storage()
    {
        // Act
        _sut.UseStorage<CustomStorage>().Build();

        // Assert
        var provider = _services.BuildServiceProvider();
        var storage = provider.GetService<IMessageStorage>();
        Assert.NotNull(storage);
        Assert.IsType<CustomStorage>(storage);
    }

    [Fact]
    public void UseStorage_Instance_Should_Register_Storage_Instance()
    {
        // Arrange
        var storageMock = new Mock<IMessageStorage>();

        // Act
        _sut.UseStorage(storageMock.Object).Build();

        // Assert
        var provider = _services.BuildServiceProvider();
        var storage = provider.GetService<IMessageStorage>();
        Assert.Same(storageMock.Object, storage);
    }

    // Test handlers
    private class TestCommandHandler : ICommandHandler<TestCommand>
    {
        public Task Handle(TestCommand command, CancellationToken cancellationToken) 
            => Task.CompletedTask;
    }
    
    private class TestQueryHandler : IQueryHandler<TestQuery, string>
    {
        public Task<string> Handle(TestQuery query, CancellationToken cancellationToken) 
            => Task.FromResult("result");
    }
    
    private class TestEventHandler : IEventHandler<TestEvent>
    {
        public Task Handle(TestEvent @event, CancellationToken cancellationToken) 
            => Task.CompletedTask;
    }
    
    private class MultipleInterfaceHandler : 
        ICommandHandler<TestCommand>,
        ICommandHandler<TestCommandWithResponse, string>
    {
        public Task Handle(TestCommand command, CancellationToken cancellationToken) 
            => Task.CompletedTask;
        
        public Task<string> Handle(TestCommandWithResponse command, CancellationToken cancellationToken) 
            => Task.FromResult("result");
    }
    
    private class CustomStorage : InMemoryMessageStorage { }
}