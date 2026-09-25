using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Serialization.Protobuf;
using ProtoBuf;
using Xunit;

namespace HeroMessaging.Serialization.Protobuf.Tests.Unit;

[Trait("Category", "Unit")]
public class TypedProtobufMessageSerializerTests
{
    [ProtoContract]
    public class TestPayload
    {
        [ProtoMember(1)]
        public string Value { get; set; } = string.Empty;
    }

    public class TypeRegistry
    {
        [Fact]
        public void ResolvesOnlyRegisteredTypes()
        {
            var registry = ProtobufTypeRegistry.CreateDefault();
            Assert.Null(registry.TryResolve(typeof(TestPayload).AssemblyQualifiedName));
            Assert.Null(registry.TryResolve(null));
            Assert.Null(registry.TryResolve(string.Empty));

            Assert.Same(registry, registry.Register<TestPayload>());
            Assert.Equal(typeof(TestPayload), registry.TryResolve(typeof(TestPayload).AssemblyQualifiedName));
            Assert.Equal(typeof(TestPayload), registry.TryResolve(typeof(TestPayload).FullName));
            Assert.Null(registry.TryResolve(typeof(string).AssemblyQualifiedName));
        }

        [Fact]
        public void NonGenericRegistrationValidatesInput()
        {
            var registry = new ProtobufTypeRegistry();
            Assert.Throws<ArgumentNullException>(() => registry.Register(null!));
            Assert.Same(registry, registry.Register(typeof(TestPayload)));
            Assert.Equal(typeof(TestPayload), registry.TryResolve(typeof(TestPayload).FullName));
        }
    }

    public class RoundTrips
    {
        [Fact]
        public async Task RegisteredTypeIsUsedByPolymorphicDeserialize()
        {
            var registry = new ProtobufTypeRegistry().Register<TestPayload>();
            var serializer = new TypedProtobufMessageSerializer(typeRegistry: registry);
            var message = new TestPayload { Value = "registered" };

            var bytes = await serializer.SerializeAsync(message, TestContext.Current.CancellationToken);
            var restored = await serializer.DeserializeAsync(bytes, typeof(object), TestContext.Current.CancellationToken);

            Assert.Equal("application/x-protobuf-typed", serializer.ContentType);
            Assert.Equal(message.Value, Assert.IsType<TestPayload>(restored).Value);
            Assert.Equal(message.Value, (await serializer.DeserializeAsync<TestPayload>(bytes, TestContext.Current.CancellationToken)).Value);
            Assert.Equal(message.Value, serializer.Deserialize<TestPayload>(bytes).Value);
            Assert.Equal(message.Value, Assert.IsType<TestPayload>(serializer.Deserialize(bytes, typeof(object))).Value);
        }

        [Fact]
        public async Task UnregisteredTypeFallsBackToRequestedType()
        {
            var serializer = new TypedProtobufMessageSerializer();
            var bytes = await serializer.SerializeAsync(new TestPayload { Value = "fallback" }, TestContext.Current.CancellationToken);

            var restored = await serializer.DeserializeAsync(bytes, typeof(TestPayload), TestContext.Current.CancellationToken);

            Assert.Equal("fallback", Assert.IsType<TestPayload>(restored).Value);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task SupportsCompressionWithAndWithoutTypeHeaders(bool includeTypeInformation)
        {
            var serializer = new TypedProtobufMessageSerializer(new SerializationOptions
            {
                EnableCompression = true,
                IncludeTypeInformation = includeTypeInformation
            });
            var message = new TestPayload { Value = new string('x', 200) };

            var bytes = await serializer.SerializeAsync(message, TestContext.Current.CancellationToken);
            var restored = await serializer.DeserializeAsync<TestPayload>(bytes, TestContext.Current.CancellationToken);

            Assert.Equal(message.Value, restored.Value);
            Assert.Equal(message.Value, Assert.IsType<TestPayload>(
                await serializer.DeserializeAsync(bytes, typeof(TestPayload), TestContext.Current.CancellationToken)).Value);
        }

        [Fact]
        public void SpanRoundTripAndSmallBufferBehavior()
        {
            var serializer = new TypedProtobufMessageSerializer();
            var message = new TestPayload { Value = "span" };
            var buffer = new byte[serializer.GetRequiredBufferSize(message)];

            Assert.True(serializer.TrySerialize(message, buffer, out var written));
            Assert.True(written > 0);
            Assert.Equal(message.Value, serializer.Deserialize<TestPayload>(buffer.AsSpan(0, written)).Value);
            Assert.Equal(message.Value, Assert.IsType<TestPayload>(serializer.Deserialize(buffer.AsSpan(0, written), typeof(TestPayload))).Value);
            Assert.False(serializer.TrySerialize(message, Array.Empty<byte>(), out var rejected));
            Assert.Equal(0, rejected);
            Assert.Throws<ArgumentException>(() => serializer.Serialize(message, Array.Empty<byte>()));
        }
    }

    public class Boundaries
    {
        [Fact]
        public async Task EmptyAndOversizedMessagesAreHandled()
        {
            var serializer = new TypedProtobufMessageSerializer();
            Assert.Empty(await serializer.SerializeAsync<TestPayload>(null!, TestContext.Current.CancellationToken));
            Assert.Null(await serializer.DeserializeAsync<TestPayload>([], TestContext.Current.CancellationToken));
            Assert.Null(await serializer.DeserializeAsync([], typeof(TestPayload), TestContext.Current.CancellationToken));
            Assert.Null(serializer.Deserialize<TestPayload>(ReadOnlySpan<byte>.Empty));
            Assert.Null(serializer.Deserialize(ReadOnlySpan<byte>.Empty, typeof(TestPayload)));
            Assert.Equal(0, serializer.Serialize<TestPayload>(null!, Array.Empty<byte>()));

            var limited = new TypedProtobufMessageSerializer(new SerializationOptions { MaxMessageSize = 1 });
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await limited.SerializeAsync(new TestPayload { Value = "too large" }, TestContext.Current.CancellationToken));
        }
    }
}
