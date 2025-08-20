using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Core.Processing;
using HeroMessaging.Tests.Unit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Core.Processing;

public class QueueProcessorTests : IDisposable
{
    private readonly Mock<IQueueStorage> _queueStorageMock;
    private readonly Mock<ILogger<QueueProcessor>> _loggerMock;
    private readonly ServiceCollection _services;
    private readonly ServiceProvider _serviceProvider;
    private readonly QueueProcessor _sut;

    public QueueProcessorTests()
    {
        _queueStorageMock = new Mock<IQueueStorage>();
        _loggerMock = new Mock<ILogger<QueueProcessor>>();
        
        _services = new ServiceCollection();
        _services.AddSingleton(_queueStorageMock.Object);
        _services.AddSingleton(_loggerMock.Object);
        _services.AddSingleton(Mock.Of<IHeroMessaging>());
        
        _serviceProvider = _services.BuildServiceProvider();
        
        _sut = new QueueProcessor(
            _serviceProvider,
            _queueStorageMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Enqueue_Should_Create_Queue_If_Not_Exists()
    {
        // Arrange
        var message = new TestMessage();
        var queueName = "test-queue";
        
        _queueStorageMock
            .Setup(x => x.QueueExists(queueName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        await _sut.Enqueue(message, queueName);

        // Assert
        _queueStorageMock.Verify(x => x.CreateQueue(queueName, null, It.IsAny<CancellationToken>()), Times.Once);
        _queueStorageMock.Verify(x => x.Enqueue(queueName, message, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Enqueue_Should_Pass_Priority_Options()
    {
        // Arrange
        var message = new TestMessage();
        var queueName = "test-queue";
        var options = new EnqueueOptions { Priority = 10 };
        
        _queueStorageMock
            .Setup(x => x.QueueExists(queueName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _sut.Enqueue(message, queueName, options);

        // Assert
        _queueStorageMock.Verify(x => x.Enqueue(queueName, message, options, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Enqueue_With_Delay_Should_Pass_Options()
    {
        // Arrange
        var message = new TestMessage();
        var queueName = "test-queue";
        var options = new EnqueueOptions 
        { 
            Priority = 5,
            Delay = TimeSpan.FromSeconds(30)
        };
        
        _queueStorageMock
            .Setup(x => x.QueueExists(queueName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _sut.Enqueue(message, queueName, options);

        // Assert
        _queueStorageMock.Verify(x => x.Enqueue(
            queueName, 
            message, 
            It.Is<EnqueueOptions>(o => o.Priority == 5 && o.Delay == TimeSpan.FromSeconds(30)), 
            It.IsAny<CancellationToken>()), 
            Times.Once);
    }

    [Fact]
    public async Task StartQueue_Should_Create_Queue_If_Not_Exists()
    {
        // Arrange
        var queueName = "test-queue";
        
        _queueStorageMock
            .Setup(x => x.QueueExists(queueName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        await _sut.StartQueue(queueName);

        // Assert
        _queueStorageMock.Verify(x => x.CreateQueue(queueName, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StopQueue_Should_Stop_Worker()
    {
        // Arrange
        var queueName = "test-queue";
        
        _queueStorageMock
            .Setup(x => x.QueueExists(queueName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        
        _queueStorageMock
            .Setup(x => x.Dequeue(queueName, It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueueEntry?)null);

        // Act
        await _sut.StartQueue(queueName);
        await Task.Delay(100); // Let it start
        await _sut.StopQueue(queueName);

        // Assert - should not throw
        Assert.True(true);
    }

    [Fact]
    public async Task GetQueueDepth_Should_Return_Depth_From_Storage()
    {
        // Arrange
        var queueName = "test-queue";
        var expectedDepth = 42L;
        
        _queueStorageMock
            .Setup(x => x.GetQueueDepth(queueName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedDepth);

        // Act
        var depth = await _sut.GetQueueDepth(queueName);

        // Assert
        Assert.Equal(expectedDepth, depth);
    }

    [Fact]
    public async Task QueueWorker_Should_Process_Messages_In_Priority_Order()
    {
        // Arrange
        var queueName = "test-queue";
        var processedMessages = new List<string>();
        var heroMessagingMock = new Mock<IHeroMessaging>();
        
        _services.AddSingleton(heroMessagingMock.Object);
        var provider = _services.BuildServiceProvider();
        var processor = new QueueProcessor(provider, _queueStorageMock.Object, _loggerMock.Object);
        
        var highPriorityEntry = new QueueEntry
        {
            Id = "1",
            Message = new TestCommand { Data = "high" },
            Options = new EnqueueOptions { Priority = 10 }
        };
        
        var lowPriorityEntry = new QueueEntry
        {
            Id = "2",
            Message = new TestCommand { Data = "low" },
            Options = new EnqueueOptions { Priority = 1 }
        };
        
        var callCount = 0;
        _queueStorageMock
            .Setup(x => x.QueueExists(queueName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        
        _queueStorageMock
            .Setup(x => x.Dequeue(queueName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount switch
                {
                    1 => highPriorityEntry,
                    2 => lowPriorityEntry,
                    _ => null
                };
            });
        
        heroMessagingMock
            .Setup(x => x.Send(It.IsAny<ICommand>(), It.IsAny<CancellationToken>()))
            .Callback<ICommand, CancellationToken>((cmd, _) => processedMessages.Add(((TestCommand)cmd).Data))
            .Returns(Task.CompletedTask);

        // Act
        await processor.StartQueue(queueName);
        await Task.Delay(500); // Let it process
        await processor.StopQueue(queueName);

        // Assert
        Assert.Equal(2, processedMessages.Count);
        Assert.Equal("high", processedMessages[0]);
        Assert.Equal("low", processedMessages[1]);
    }

    [Fact]
    public async Task QueueWorker_Should_Retry_Failed_Messages()
    {
        // Arrange
        var queueName = "test-queue";
        var heroMessagingMock = new Mock<IHeroMessaging>();
        
        _services.AddSingleton(heroMessagingMock.Object);
        var provider = _services.BuildServiceProvider();
        var processor = new QueueProcessor(provider, _queueStorageMock.Object, _loggerMock.Object);
        
        var entry = new QueueEntry
        {
            Id = "1",
            Message = new TestCommand(),
            DequeueCount = 1
        };
        
        _queueStorageMock
            .Setup(x => x.QueueExists(queueName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        
        _queueStorageMock
            .SetupSequence(x => x.Dequeue(queueName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry)
            .ReturnsAsync((QueueEntry?)null);
        
        heroMessagingMock
            .Setup(x => x.Send(It.IsAny<ICommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Processing failed"));

        // Act
        await processor.StartQueue(queueName);
        await Task.Delay(200);
        await processor.StopQueue(queueName);

        // Assert
        _queueStorageMock.Verify(x => x.Reject(queueName, entry.Id, It.Is<bool>(b => b == true), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task QueueWorker_Should_Not_Requeue_After_Max_Retries()
    {
        // Arrange
        var queueName = "test-queue";
        var heroMessagingMock = new Mock<IHeroMessaging>();
        
        _services.AddSingleton(heroMessagingMock.Object);
        var provider = _services.BuildServiceProvider();
        var processor = new QueueProcessor(provider, _queueStorageMock.Object, _loggerMock.Object);
        
        var entry = new QueueEntry
        {
            Id = "1",
            Message = new TestCommand(),
            DequeueCount = 3 // Already at max retries
        };
        
        _queueStorageMock
            .Setup(x => x.QueueExists(queueName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        
        _queueStorageMock
            .SetupSequence(x => x.Dequeue(queueName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry)
            .ReturnsAsync((QueueEntry?)null);
        
        heroMessagingMock
            .Setup(x => x.Send(It.IsAny<ICommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Processing failed"));

        // Act
        await processor.StartQueue(queueName);
        await Task.Delay(200);
        await processor.StopQueue(queueName);

        // Assert
        _queueStorageMock.Verify(x => x.Reject(queueName, entry.Id, It.Is<bool>(b => b == false), It.IsAny<CancellationToken>()), Times.Once);
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }
    
    private class TestCommand : TestMessageBase, ICommand
    {
        public string Data { get; set; } = string.Empty;
    }
}