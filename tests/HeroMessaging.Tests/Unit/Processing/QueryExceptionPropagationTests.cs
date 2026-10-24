using System.Reflection;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Abstractions.Queries;
using HeroMessaging.Processing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeroMessaging.Tests.Unit.Processing;

[Trait("Category", "Unit")]
public sealed class QueryExceptionPropagationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandlerFailure_PreservesExceptionAndAllowsNextQuery(bool returnsFaultedTask)
    {
        var failure = new InvalidOperationException("query handler rejected input");
        var handler = new FailingOnceHandler(failure, returnsFaultedTask);
        using var services = new ServiceCollection()
            .AddSingleton<IQueryHandler<TestQuery, string>>(handler).BuildServiceProvider();
        await using var processor = new QueryProcessor(services);
        var query = new TestQuery();

        var error = await Record.ExceptionAsync(() => processor.SendAsync(query, TestContext.Current.CancellationToken));

        Assert.NotNull(error);
        Assert.Same(failure, error);
        Assert.Equal(1, processor.GetMetrics().FailedCount);
        Assert.Equal(0, processor.GetMetrics().ProcessedCount);
        if (!returnsFaultedTask)
            Assert.Contains(nameof(FailingOnceHandler.HandleAsync), error.StackTrace, StringComparison.Ordinal);

        var response = await processor.SendAsync(query, TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal("recovered", response);
        Assert.Equal(2, handler.Calls);
        Assert.Equal(1, processor.GetMetrics().FailedCount);
        Assert.Equal(1, processor.GetMetrics().ProcessedCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandlerCancellation_PreservesTokenWithoutCountingFailure(bool returnsCanceledTask)
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new CancelingOnceHandler(cancellation, returnsCanceledTask);
        using var services = new ServiceCollection()
            .AddSingleton<IQueryHandler<TestQuery, string>>(handler).BuildServiceProvider();
        await using var processor = new QueryProcessor(services);

        var execution = processor.SendAsync(new TestQuery(), cancellation.Token);
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.True(execution.IsCanceled);
        Assert.Equal(0, processor.GetMetrics().FailedCount);
        Assert.Equal(0, processor.GetMetrics().ProcessedCount);
        var response = await processor.SendAsync(new TestQuery(), TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal("recovered", response);
        Assert.Equal(0, processor.GetMetrics().FailedCount);
        Assert.Equal(1, processor.GetMetrics().ProcessedCount);
    }

    [Fact]
    public async Task HandlerOwnedTargetInvocationException_IsPreserved()
    {
        var inner = new InvalidOperationException("handler-owned inner failure");
        var failure = new TargetInvocationException("handler-owned wrapper", inner);
        var handler = new FailingOnceHandler(failure, false);
        using var services = new ServiceCollection()
            .AddSingleton<IQueryHandler<TestQuery, string>>(handler).BuildServiceProvider();
        await using var processor = new QueryProcessor(services);

        var error = await Assert.ThrowsAsync<TargetInvocationException>(() =>
            processor.SendAsync(new TestQuery(), TestContext.Current.CancellationToken));

        Assert.Same(failure, error);
        Assert.Same(inner, error.InnerException);
        Assert.Equal(1, processor.GetMetrics().FailedCount);
    }

    private sealed class FailingOnceHandler(Exception failure, bool returnsFaultedTask) : IQueryHandler<TestQuery, string>
    {
        public int Calls { get; private set; }

        public Task<string> HandleAsync(TestQuery query, CancellationToken cancellationToken)
        {
            if (++Calls > 1)
                return Task.FromResult("recovered");

            if (returnsFaultedTask)
                return Task.FromException<string>(failure);

            throw failure;
        }
    }

    private sealed class CancelingOnceHandler(CancellationTokenSource cancellation, bool returnsCanceledTask) : IQueryHandler<TestQuery, string>
    {
        private bool _firstCall = true;

        public Task<string> HandleAsync(TestQuery query, CancellationToken cancellationToken)
        {
            if (!_firstCall)
                return Task.FromResult("recovered");

            _firstCall = false;
            cancellation.Cancel();
            if (returnsCanceledTask)
                return Task.FromCanceled<string>(cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult("unreachable");
        }
    }

    private sealed class TestQuery : IQuery<string>
    {
        public Guid MessageId { get; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; } = DateTimeOffset.UnixEpoch;
        public string? CorrelationId { get; }
        public string? CausationId { get; }
        public Dictionary<string, object>? Metadata { get; }
    }
}
