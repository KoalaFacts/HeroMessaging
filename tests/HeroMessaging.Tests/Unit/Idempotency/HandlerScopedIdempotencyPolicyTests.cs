using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Idempotency;
using Xunit;

namespace HeroMessaging.Tests.Unit.Idempotency;

[Trait("Category", "Unit")]
public sealed class HandlerScopedIdempotencyPolicyTests
{
    [Fact]
    public void GenerateKey_WithHandler_AppendsHandlerType()
    {
        var policy = new HandlerScopedIdempotencyPolicy(new DefaultIdempotencyPolicy());
        var messageId = Guid.NewGuid();
        var message = new TestMessage { MessageId = messageId };

        var key = policy.KeyGenerator.GenerateKey(message, new ProcessingContext { Handler = new HandlerA() });

        Assert.Equal($"idempotency:{messageId}:handler:{typeof(HandlerA).FullName}", key);
    }

    [Fact]
    public void GenerateKey_DifferentHandlers_ProduceDifferentKeys()
    {
        var policy = new HandlerScopedIdempotencyPolicy(new DefaultIdempotencyPolicy());
        var message = new TestMessage { MessageId = Guid.NewGuid() };

        var first = policy.KeyGenerator.GenerateKey(message, new ProcessingContext { Handler = new HandlerA() });
        var second = policy.KeyGenerator.GenerateKey(message, new ProcessingContext { Handler = new HandlerB() });

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void GenerateKey_WithoutHandler_KeepsConfiguredKey()
    {
        var policy = new HandlerScopedIdempotencyPolicy(new DefaultIdempotencyPolicy());
        var messageId = Guid.NewGuid();

        var key = policy.KeyGenerator.GenerateKey(new TestMessage { MessageId = messageId }, new ProcessingContext());

        Assert.Equal($"idempotency:{messageId}", key);
    }

    [Fact]
    public void Settings_DelegateToInnerPolicy()
    {
        var inner = new DefaultIdempotencyPolicy(TimeSpan.FromHours(2), TimeSpan.FromMinutes(5), cacheFailures: false);
        var policy = new HandlerScopedIdempotencyPolicy(inner);

        Assert.Same(inner, policy.Inner);
        Assert.Equal(TimeSpan.FromHours(2), policy.SuccessTtl);
        Assert.Equal(TimeSpan.FromMinutes(5), policy.FailureTtl);
        Assert.False(policy.CacheFailures);
        Assert.True(policy.IsIdempotentFailure(new ArgumentException("bad")));
        Assert.False(policy.IsIdempotentFailure(new TimeoutException()));
    }

    [Fact]
    public void GenerateKey_GenericHandler_OmitsAssemblyVersions()
    {
        var policy = new HandlerScopedIdempotencyPolicy(new DefaultIdempotencyPolicy());
        var messageId = Guid.NewGuid();

        var key = policy.KeyGenerator.GenerateKey(new TestMessage { MessageId = messageId },
            new ProcessingContext { Handler = new GenericHandler<HandlerA>() });

        var scope = typeof(HandlerScopedIdempotencyPolicyTests).FullName;
        Assert.Equal($"idempotency:{messageId}:handler:{scope}+GenericHandler`1[{scope}+HandlerA]", key);
        Assert.DoesNotContain("Version=", key);
    }

    [Fact]
    public void Constructor_NullInner_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new HandlerScopedIdempotencyPolicy(null!));
    }

    private sealed record TestMessage : Abstractions.Messages.IMessage
    {
        public Guid MessageId { get; init; }
        public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; init; }
        public string? CausationId { get; init; }
        public Dictionary<string, object>? Metadata { get; init; }
    }

    private sealed class HandlerA;

    private sealed class HandlerB;

    private sealed class GenericHandler<T>;
}
