using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Serialization.MessagePack;
using MessagePack;
using Xunit;

namespace HeroMessaging.Serialization.MessagePack.Tests.Unit;

[Trait("Category", "Unit")]
public class ContractMessagePackSerializerTests
{
    [MessagePackObject]
    public class TestPayload
    {
        [Key(0)]
        public string Value { get; set; } = string.Empty;
    }

    public class RoundTrips
    {
        [Fact]
        public async Task AsyncAndSpanApisPreserveContract()
        {
            var serializer = new ContractMessagePackSerializer();
            var message = new TestPayload { Value = "contract" };

            var bytes = await serializer.SerializeAsync(message, TestContext.Current.CancellationToken);
            Assert.Equal("application/x-msgpack-contract", serializer.ContentType);
            Assert.Equal(message.Value, (await serializer.DeserializeAsync<TestPayload>(bytes, TestContext.Current.CancellationToken)).Value);
            Assert.Equal(message.Value, Assert.IsType<TestPayload>(
                await serializer.DeserializeAsync(bytes, typeof(TestPayload), TestContext.Current.CancellationToken)).Value);

            var buffer = new byte[serializer.GetRequiredBufferSize(message)];
            Assert.True(serializer.TrySerialize(message, buffer, out var written));
            Assert.Equal(message.Value, serializer.Deserialize<TestPayload>(buffer.AsSpan(0, written)).Value);
            Assert.Equal(message.Value, Assert.IsType<TestPayload>(serializer.Deserialize(buffer.AsSpan(0, written), typeof(TestPayload))).Value);
        }

        [Fact]
        public async Task ExternalCompressionRoundTrips()
        {
            var serializer = new ContractMessagePackSerializer(new SerializationOptions { EnableCompression = true });
            var message = new TestPayload { Value = new string('x', 200) };

            var bytes = await serializer.SerializeAsync(message, TestContext.Current.CancellationToken);

            Assert.Equal(message.Value, (await serializer.DeserializeAsync<TestPayload>(bytes, TestContext.Current.CancellationToken)).Value);
            Assert.Equal(message.Value, Assert.IsType<TestPayload>(
                await serializer.DeserializeAsync(bytes, typeof(TestPayload), TestContext.Current.CancellationToken)).Value);
        }
    }

    public class Boundaries
    {
        [Fact]
        public async Task EmptyAndOversizedMessagesAreHandled()
        {
            var serializer = new ContractMessagePackSerializer();
            Assert.Empty(await serializer.SerializeAsync<TestPayload>(null!, TestContext.Current.CancellationToken));
            Assert.Null(await serializer.DeserializeAsync<TestPayload>([], TestContext.Current.CancellationToken));
            Assert.Null(await serializer.DeserializeAsync([], typeof(TestPayload), TestContext.Current.CancellationToken));
            Assert.Null(serializer.Deserialize<TestPayload>(ReadOnlySpan<byte>.Empty));
            Assert.Null(serializer.Deserialize(ReadOnlySpan<byte>.Empty, typeof(TestPayload)));
            Assert.Equal(0, serializer.Serialize<TestPayload>(null!, Array.Empty<byte>()));

            var message = new TestPayload { Value = "too large" };
            var limited = new ContractMessagePackSerializer(new SerializationOptions { MaxMessageSize = 1 });
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await limited.SerializeAsync(message, TestContext.Current.CancellationToken));
            Assert.False(serializer.TrySerialize(message, Array.Empty<byte>(), out var written));
            Assert.Equal(0, written);
            Assert.Throws<ArgumentException>(() => serializer.Serialize(message, Array.Empty<byte>()));
        }
    }
}
