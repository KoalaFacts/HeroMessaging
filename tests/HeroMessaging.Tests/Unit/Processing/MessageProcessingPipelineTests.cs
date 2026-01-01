using System.Collections.Generic;
using System.Linq;
using HeroMessaging.Abstractions.ErrorHandling;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Metrics;
using HeroMessaging.Abstractions.Policies;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Validation;
using HeroMessaging.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Processing;

[Trait("Category", "Unit")]
public sealed class MessageProcessingPipelineTests : IDisposable
{
    private readonly ServiceCollection _services;
    private readonly ServiceProvider _serviceProvider;

    public MessageProcessingPipelineTests()
    {
        _services = new ServiceCollection();
        _services.AddSingleton(TimeProvider.System);
        _serviceProvider = _services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }

    private MessageProcessingPipelineBuilder CreateBuilder()
    {
        var services = new ServiceCollection();

        // Copy existing registrations
        foreach (var service in _services)
        {
            ((IList<ServiceDescriptor>)services).Add(service);
        }

        var provider = services.BuildServiceProvider();
        return new MessageProcessingPipelineBuilder(provider);
    }

    #region Builder Pattern Tests

    [Fact]
    public void Build_WithNoDecorators_ReturnsInnerProcessor()
    {
        // Arrange
        var builder = CreateBuilder();
        var innerProcessor = new Mock<IMessageProcessor>();

        // Act
        var pipeline = builder.Build(innerProcessor.Object);

        // Assert
        Assert.NotNull(pipeline);
    }

    [Fact]
    public async Task Build_WithLogging_AddsLoggingDecorator()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<HeroMessaging.Processing.Decorators.LoggingDecorator>>();
        _services.AddSingleton(loggerMock.Object);

        var builder = CreateBuilder();
        var innerProcessor = new Mock<IMessageProcessor>();
        var message = new TestMessage();
        var context = new ProcessingContext();

        innerProcessor
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act
        var pipeline = builder
            .UseLogging()
            .Build(innerProcessor.Object);

        var result = await pipeline.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        innerProcessor.Verify(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Build_WithValidation_AddsValidationDecorator()
    {
        // Arrange
        var validatorMock = new Mock<IMessageValidator>();
        _services.AddSingleton(validatorMock.Object);

        var builder = CreateBuilder();
        var innerProcessor = new Mock<IMessageProcessor>();
        var message = new TestMessage();
        var context = new ProcessingContext();

        validatorMock
            .Setup(v => v.ValidateAsync(message, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult { IsValid = true });

        innerProcessor
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act
        var pipeline = builder
            .UseValidation()
            .Build(innerProcessor.Object);

        var result = await pipeline.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        validatorMock.Verify(v => v.ValidateAsync(message, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Build_WithRetry_AddsRetryDecorator()
    {
        // Arrange
        var retryPolicyMock = new Mock<IRetryPolicy>();
        retryPolicyMock.Setup(p => p.MaxRetries).Returns(0); // No retries for this test

        var builder = CreateBuilder();
        var innerProcessor = new Mock<IMessageProcessor>();
        var message = new TestMessage();
        var context = new ProcessingContext();

        innerProcessor
            .Setup(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act
        var pipeline = builder
            .UseRetry(retryPolicyMock.Object)
            .Build(innerProcessor.Object);

        var result = await pipeline.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
    }

    [Fact]
    public async Task Build_WithErrorHandling_AddsErrorHandlingDecorator()
    {
        // Arrange
        var errorHandlerMock = new Mock<IErrorHandler>();
        _services.AddSingleton(errorHandlerMock.Object);

        var builder = CreateBuilder();
        var innerProcessor = new Mock<IMessageProcessor>();
        var message = new TestMessage();
        var context = new ProcessingContext();

        innerProcessor
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act
        var pipeline = builder
            .UseErrorHandling()
            .Build(innerProcessor.Object);

        var result = await pipeline.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
    }

    [Fact]
    public async Task Build_WithMetrics_AddsMetricsDecorator()
    {
        // Arrange
        var metricsCollectorMock = new Mock<IMetricsCollector>();
        _services.AddSingleton(metricsCollectorMock.Object);

        var builder = CreateBuilder();
        var innerProcessor = new Mock<IMessageProcessor>();
        var message = new TestMessage();
        var context = new ProcessingContext();

        innerProcessor
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        metricsCollectorMock
            .Setup(m => m.RecordDuration(It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .Verifiable();

        // Act
        var pipeline = builder
            .UseMetrics()
            .Build(innerProcessor.Object);

        var result = await pipeline.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        metricsCollectorMock.Verify(m => m.IncrementCounter(It.IsAny<string>(), It.IsAny<int>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Build_WithCircuitBreaker_AddsCircuitBreakerDecorator()
    {
        // Arrange
        var builder = CreateBuilder();
        var innerProcessor = new Mock<IMessageProcessor>();
        var message = new TestMessage();
        var context = new ProcessingContext();

        innerProcessor
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act
        var pipeline = builder
            .UseCircuitBreaker()
            .Build(innerProcessor.Object);

        var result = await pipeline.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
    }

    [Fact]
    public async Task Build_WithCorrelation_AddsCorrelationDecorator()
    {
        // Arrange
        var builder = CreateBuilder();
        var innerProcessor = new Mock<IMessageProcessor>();
        var message = new TestMessage { CorrelationId = "correlation-123" };
        var context = new ProcessingContext();

        innerProcessor
            .Setup(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act
        var pipeline = builder
            .UseCorrelation()
            .Build(innerProcessor.Object);

        var result = await pipeline.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
    }

    #endregion

    #region Multiple Decorators

    [Fact]
    public async Task Build_WithMultipleDecorators_AppliesInCorrectOrder()
    {
        // Arrange
        var validatorMock = new Mock<IMessageValidator>();
        var metricsCollectorMock = new Mock<IMetricsCollector>();
        _services.AddSingleton(validatorMock.Object);
        _services.AddSingleton(metricsCollectorMock.Object);

        var builder = CreateBuilder();
        var innerProcessor = new Mock<IMessageProcessor>();
        var message = new TestMessage();
        var context = new ProcessingContext();

        validatorMock
            .Setup(v => v.ValidateAsync(message, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult { IsValid = true });

        innerProcessor
            .Setup(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act
        var pipeline = builder
            .UseMetrics()
            .UseLogging()
            .UseCorrelation()
            .UseValidation()
            .UseRetry()
            .Build(innerProcessor.Object);

        var result = await pipeline.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        validatorMock.Verify(v => v.ValidateAsync(message, It.IsAny<CancellationToken>()), Times.Once);
        metricsCollectorMock.Verify(m => m.IncrementCounter(It.IsAny<string>(), It.IsAny<int>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Build_WithFullPipeline_ProcessesMessageThroughAllDecorators()
    {
        // Arrange
        var validatorMock = new Mock<IMessageValidator>();
        var errorHandlerMock = new Mock<IErrorHandler>();
        var metricsCollectorMock = new Mock<IMetricsCollector>();

        _services.AddSingleton(validatorMock.Object);
        _services.AddSingleton(errorHandlerMock.Object);
        _services.AddSingleton(metricsCollectorMock.Object);

        var builder = CreateBuilder();
        var innerProcessor = new Mock<IMessageProcessor>();
        var message = new TestMessage();
        var context = new ProcessingContext();

        validatorMock
            .Setup(v => v.ValidateAsync(message, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult { IsValid = true });

        innerProcessor
            .Setup(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act
        var pipeline = builder
            .UseMetrics()
            .UseLogging()
            .UseCorrelation()
            .UseValidation()
            .UseErrorHandling()
            .UseRetry()
            .Build(innerProcessor.Object);

        var result = await pipeline.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        innerProcessor.Verify(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Custom Decorator

    [Fact]
    public async Task Build_WithCustomDecorator_AppliesCustomLogic()
    {
        // Arrange
        var builder = CreateBuilder();
        var innerProcessor = new Mock<IMessageProcessor>();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var customDecoratorCalled = false;

        innerProcessor
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act
        var pipeline = builder
            .Use(inner => new CustomTestDecorator(inner, () => customDecoratorCalled = true))
            .Build(innerProcessor.Object);

        var result = await pipeline.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.True(customDecoratorCalled);
    }

    #endregion

    #region CoreMessageProcessor

    [Fact]
    public async Task CoreMessageProcessor_WithSuccessfulFunc_ReturnsSuccess()
    {
        // Arrange
        var processedMessage = (IMessage?)null;
        var processor = new CoreMessageProcessor((msg, ctx, ct) =>
        {
            processedMessage = msg;
            return ValueTask.CompletedTask;
        });

        var message = new TestMessage();
        var context = new ProcessingContext();

        // Act
        var result = await processor.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(message, processedMessage);
    }

    [Fact]
    public async Task CoreMessageProcessor_WithException_ReturnsFailure()
    {
        // Arrange
        var processor = new CoreMessageProcessor((msg, ctx, ct) =>
        {
            throw new InvalidOperationException("Test exception");
        });

        var message = new TestMessage();
        var context = new ProcessingContext();

        // Act
        var result = await processor.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.Exception);
        Assert.IsType<InvalidOperationException>(result.Exception);
    }

    #endregion

    #region Decorator Skipping

    [Fact]
    public async Task Build_WithMissingValidator_SkipsValidationDecorator()
    {
        // Arrange - No validator registered
        var builder = CreateBuilder();
        var innerProcessor = new Mock<IMessageProcessor>();
        var message = new TestMessage();
        var context = new ProcessingContext();

        innerProcessor
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act
        var pipeline = builder
            .UseValidation()
            .Build(innerProcessor.Object);

        var result = await pipeline.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert - Should still work without validator
        Assert.True(result.Success);
    }

    [Fact]
    public async Task Build_WithMissingErrorHandler_SkipsErrorHandlingDecorator()
    {
        // Arrange - No error handler registered
        var builder = CreateBuilder();
        var innerProcessor = new Mock<IMessageProcessor>();
        var message = new TestMessage();
        var context = new ProcessingContext();

        innerProcessor
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act
        var pipeline = builder
            .UseErrorHandling()
            .Build(innerProcessor.Object);

        var result = await pipeline.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert - Should still work without error handler
        Assert.True(result.Success);
    }

    [Fact]
    public async Task Build_WithMissingMetricsCollector_SkipsMetricsDecorator()
    {
        // Arrange - No metrics collector registered
        var builder = CreateBuilder();
        var innerProcessor = new Mock<IMessageProcessor>();
        var message = new TestMessage();
        var context = new ProcessingContext();

        innerProcessor
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act
        var pipeline = builder
            .UseMetrics()
            .Build(innerProcessor.Object);

        var result = await pipeline.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert - Should still work without metrics collector
        Assert.True(result.Success);
    }

    [Fact]
    public async Task Build_WithMissingRateLimiter_SkipsRateLimitingDecorator()
    {
        // Arrange - No rate limiter registered
        var builder = CreateBuilder();
        var innerProcessor = new Mock<IMessageProcessor>();
        var message = new TestMessage();
        var context = new ProcessingContext();

        innerProcessor
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act
        var pipeline = builder
            .UseRateLimiting()
            .Build(innerProcessor.Object);

        var result = await pipeline.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert - Should still work without rate limiter
        Assert.True(result.Success);
    }

    #endregion

    #region Test Helper Classes

    public class TestMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; } = [];
    }

    public class CustomTestDecorator : IMessageProcessor
    {
        private readonly IMessageProcessor _inner;
        private readonly Action _onProcess;

        public CustomTestDecorator(IMessageProcessor inner, Action onProcess)
        {
            _inner = inner;
            _onProcess = onProcess;
        }

        public async ValueTask<ProcessingResult> ProcessAsync(
            IMessage message,
            ProcessingContext context,
            CancellationToken cancellationToken = default)
        {
            _onProcess();
            return await _inner.ProcessAsync(message, context, cancellationToken);
        }
    }

    #endregion
}
