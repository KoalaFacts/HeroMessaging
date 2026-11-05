using System.Reflection;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Abstractions.Queries;
using HeroMessaging.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Runtime.CompilerServices;

namespace HeroMessaging.AotSmoke;

internal static class QueryScenarios
{
    public static async Task RunAsync()
    {
        using (var provider = Provider(new ProbeHandler((query, _) => Task.FromResult(query.Value))))
        {
            await using var processor = new QueryProcessor(provider);
            Require(await processor.SendAsync(new ProbeQuery("answer")).WaitAsync(TimeSpan.FromSeconds(5)) == "answer", "query result");
            Require(processor.GetMetrics().ProcessedCount == 1 && processor.GetMetrics().FailedCount == 0, "success metrics");
        }

        foreach (var asynchronous in new[] { false, true })
        {
            var failure = new InvalidOperationException("intentional query failure");
            var calls = 0;
            using var provider = Provider(new ProbeHandler((_, _) =>
            {
                if (++calls > 1) return Task.FromResult("recovered");
                if (asynchronous) return Task.FromException<string>(failure);
                throw failure;
            }));
            await using var processor = new QueryProcessor(provider);
            var observed = await Capture(processor.SendAsync(new ProbeQuery("fail")));
            Require(ReferenceEquals(failure, observed), "handler exception object identity");
            Require(processor.GetMetrics().FailedCount == 1 && processor.GetMetrics().ProcessedCount == 0, "failure metrics");
            Require(await processor.SendAsync(new ProbeQuery("next")).WaitAsync(TimeSpan.FromSeconds(5)) == "recovered", "lock released after failure");
            Require(calls == 2 && processor.GetMetrics().FailedCount == 1 && processor.GetMetrics().ProcessedCount == 1, "recovery metrics");
        }

        foreach (var asynchronous in new[] { false, true })
        {
            using var cancellation = new CancellationTokenSource();
            var calls = 0;
            using var provider = Provider(new ProbeHandler((_, token) =>
            {
                if (++calls > 1) return Task.FromResult("recovered");
                cancellation.Cancel();
                if (asynchronous) return Task.FromCanceled<string>(token);
                token.ThrowIfCancellationRequested();
                return Task.FromResult("unreachable");
            }));
            await using var processor = new QueryProcessor(provider);
            var task = processor.SendAsync(new ProbeQuery("cancel"), cancellation.Token);
            var observed = await Capture(task);
            Require(observed is OperationCanceledException canceled && canceled.CancellationToken == cancellation.Token, "caller cancellation token identity");
            Require(task.IsCanceled && processor.GetMetrics().FailedCount == 0 && processor.GetMetrics().ProcessedCount == 0, "cancellation task status and metrics");
            Require(await processor.SendAsync(new ProbeQuery("next")).WaitAsync(TimeSpan.FromSeconds(5)) == "recovered", "lock released after cancellation");
            Require(calls == 2 && processor.GetMetrics().FailedCount == 0 && processor.GetMetrics().ProcessedCount == 1, "cancellation recovery metrics");
        }

        var inner = new InvalidOperationException("handler-owned inner");
        var wrapper = new TargetInvocationException("handler-owned wrapper", inner);
        using (var provider = Provider(new ProbeHandler((_, _) => throw wrapper)))
        {
            await using var processor = new QueryProcessor(provider);
            var observed = await Capture(processor.SendAsync(new ProbeQuery("wrapper")));
            Require(ReferenceEquals(wrapper, observed) && ReferenceEquals(observed.InnerException, inner), "handler-owned wrapper identity");
            Require(processor.GetMetrics().FailedCount == 1, "wrapper failure counted once");
        }

        using (var provider = Provider(new ProbeHandler((query, _) => Task.FromResult(query.Value))))
        {
            var processor = new QueryProcessor(provider);
            await processor.DisposeAsync();
            Require(!processor.IsRunning && await Capture(processor.SendAsync(new ProbeQuery("disposed"))) is ObjectDisposedException, "disposed processor rejects query");
        }
        using (var provider = Services(static (query, _) => Task.FromResult(query.Value)).BuildServiceProvider())
        {
            await using var processor = new QueryProcessor(provider);
            Require(await processor.SendAsync(new ValueQuery()) == 42, "value response");
            Require(await processor.SendAsync(new OtherQuery()) == "other" &&
                await processor.SendAsync(new ProbeQuery("first")) == "first", "query types with shared response stay distinct");
        }

        var overrideState = new QueryState();
        using (var provider = Services(static (_, _) => Task.FromResult("original"))
            .AddSingleton<IQueryHandler<ProbeQuery, string>>(new ProbeHandler(static (_, _) => Task.FromResult("override"), overrideState))
            .BuildServiceProvider())
        {
            await using var processor = new QueryProcessor(provider);
            Require(await processor.SendAsync(new ProbeQuery("input")) == "override", "last handler registration wins");
        }

        var scopeState = new QueryState();
        var scopedServices = Services(static (query, _) => Task.FromResult(query.Value), scopeState);
        scopedServices.RemoveAll<IQueryHandler<ProbeQuery, string>>();
        scopedServices.AddScoped<IQueryHandler<ProbeQuery, string>, ProbeHandler>();
        await using (var provider = scopedServices.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }))
        {
            await using (var scope = provider.CreateAsyncScope())
            {
                await using var processor = new QueryProcessor(scope.ServiceProvider);
                Require(await processor.SendAsync(new ProbeQuery("one")) == "one" &&
                    await processor.SendAsync(new ProbeQuery("two")) == "two", "scoped handler results");
                Require(scopeState.Constructions == 1 && scopeState.Disposals == 0, "scope owns shared handler lifetime");
            }
            Require(scopeState.Disposals == 1, "scope disposes handler once");
        }
        Require(scopeState.Disposals == 1, "provider does not double dispose scoped handler");

        var missing = Services(static (_, _) => Task.FromResult("unused"));
        missing.RemoveAll<IQueryHandler<ProbeQuery, string>>();
        using (var provider = missing.BuildServiceProvider())
        {
            await using var processor = new QueryProcessor(provider);
            var error = await Capture(processor.SendAsync(new ProbeQuery("missing")));
            Require(error is InvalidOperationException && error.Message == "No handler found for query type ProbeQuery", "missing handler error contract");
            Require(processor.GetMetrics().FailedCount == 0, "resolution errors precede processing metrics");
        }

        using var preCanceled = new CancellationTokenSource();
        preCanceled.Cancel();
        var constructorFailure = new InvalidOperationException("constructor failure");
        using (var provider = Services(static (_, _) => Task.FromResult("unused"), new QueryState { ConstructorFailure = constructorFailure }).BuildServiceProvider())
        {
            await using var processor = new QueryProcessor(provider);
            Require(ReferenceEquals(await Capture(processor.SendAsync(new ProbeQuery("input"), preCanceled.Token)), constructorFailure), "constructor failure precedes cancellation");
        }
        var canceledState = new QueryState();
        using (var provider = Services(static (_, _) => throw new InvalidOperationException("canceled handler invoked"), canceledState).BuildServiceProvider())
        {
            await using var processor = new QueryProcessor(provider);
            var execution = processor.SendAsync(new ProbeQuery("input"), preCanceled.Token);
            Require(await Capture(execution) is OperationCanceledException error && error.CancellationToken == preCanceled.Token && execution.IsCanceled,
                "pre-canceled request identity");
            Require(canceledState.Constructions == 1 && canceledState.Calls == 0, "resolve before cancellation but do not invoke");
        }

        using (var provider = Services(static (query, _) => Task.FromResult(query.Value)).BuildServiceProvider())
        {
            await using var processor = new QueryProcessor(new PlainProvider(provider));
            if (RuntimeFeature.IsDynamicCodeSupported)
                Require(await processor.SendAsync(new ProbeQuery("plain")) == "plain", "custom non-keyed JIT provider");
            else
                Require(await Capture(processor.SendAsync(new ProbeQuery("plain"))) is InvalidOperationException error &&
                    error.Message.Contains("AddGeneratedHandlers", StringComparison.Ordinal), "unsupported custom AOT provider fails explicitly");
        }
        Console.WriteLine("QUERY_SCENARIOS_PASS scenarios=15 success/error/cancellation/wrapper/disposal/value/distinct/override/scope/missing/constructor/precancel/provider");
    }

    private static IServiceCollection Services(Func<ProbeQuery, CancellationToken, Task<string>> execute, QueryState? state = null) =>
        new ServiceCollection()
            .AddSingleton(new ProbeState())
            .AddSingleton<HeroMessaging.Abstractions.Storage.IBasicMessageStorage>(new HeroMessaging.Storage.InMemoryMessageStorage(TimeProvider.System))
            .AddSingleton(state ?? new QueryState())
            .AddSingleton(execute).AddGeneratedHandlers();

    private static ServiceProvider Provider(ProbeHandler handler) => Services(handler.Execute)
        .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

    private static async Task<Exception> Capture(Task<string> task)
    {
        try { await task.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (Exception exception) { return exception; }
        throw new InvalidOperationException("Expected query failure did not occur.");
    }

    private static void Require(bool condition, string behavior)
    {
        if (!condition) throw new InvalidOperationException("Query behavior failed: " + behavior);
    }

    internal sealed class ProbeHandler(Func<ProbeQuery, CancellationToken, Task<string>> execute, QueryState? state = null) : IQueryHandler<ProbeQuery, string>, IDisposable
    {
        private readonly QueryState _state = Initialize(state);
        public Func<ProbeQuery, CancellationToken, Task<string>> Execute { get; } = execute;
        public Task<string> HandleAsync(ProbeQuery query, CancellationToken cancellationToken)
        {
            _state.Calls++;
            return Execute(query, cancellationToken);
        }
        public void Dispose() => _state.Disposals++;
        private static QueryState Initialize(QueryState? state)
        {
            state ??= new QueryState();
            if (state.ConstructorFailure is not null) throw state.ConstructorFailure;
            state.Constructions++;
            return state;
        }
    }

    internal sealed class QueryState
    {
        public int Constructions { get; set; }
        public int Disposals { get; set; }
        public int Calls { get; set; }
        public Exception? ConstructorFailure { get; set; }
    }

    private sealed class PlainProvider(IServiceProvider inner) : IServiceProvider
    {
        public object? GetService(Type serviceType) => inner.GetService(serviceType);
    }

    internal sealed class ValueQuery : Message, IQuery<int>;
    internal sealed class OtherQuery : Message, IQuery<string>;
    internal sealed class ValueHandler : IQueryHandler<ValueQuery, int>
    {
        public Task<int> HandleAsync(ValueQuery query, CancellationToken cancellationToken) => Task.FromResult(42);
    }
    internal sealed class OtherHandler : IQueryHandler<OtherQuery, string>
    {
        public Task<string> HandleAsync(OtherQuery query, CancellationToken cancellationToken) => Task.FromResult("other");
    }
    internal sealed class ProbeQuery(string value) : Message, IQuery<string>
    {
        public string Value { get; } = value;
    }
    internal abstract class Message
    {
        public Guid MessageId { get; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; } = DateTimeOffset.UnixEpoch;
        public string? CorrelationId => null;
        public string? CausationId => null;
        public Dictionary<string, object>? Metadata => null;
    }
}
