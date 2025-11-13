using HeroMessaging.Abstractions.Transport;

namespace HeroMessaging.Abstractions.Tests.Transport;

[Trait("Category", "Unit")]
public class TransportAddressTests
{
    [Fact]
    public void Constructor_WithName_CreatesQueueByDefault()
    {
        // Arrange & Act
        var address = new TransportAddress("my-queue");

        // Assert
        Assert.Equal("my-queue", address.Name);
        Assert.Equal(TransportAddressType.Queue, address.Type);
        Assert.Null(address.Scheme);
        Assert.Null(address.Host);
        Assert.Null(address.Port);
    }

    [Fact]
    public void Constructor_WithNameAndType_SetsCorrectly()
    {
        // Arrange & Act
        var address = new TransportAddress("my-topic", TransportAddressType.Topic);

        // Assert
        Assert.Equal("my-topic", address.Name);
        Assert.Equal(TransportAddressType.Topic, address.Type);
    }

    [Fact]
    public void Constructor_WithNullName_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new TransportAddress(null!));
        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithUri_ParsesCorrectly()
    {
        // Arrange
        var uri = new Uri("rabbitmq://localhost:5672/queues/my-queue");

        // Act
        var address = new TransportAddress(uri);

        // Assert
        Assert.Equal("my-queue", address.Name);
        Assert.Equal("rabbitmq", address.Scheme);
        Assert.Equal("localhost", address.Host);
        Assert.Equal(5672, address.Port);
    }

    [Fact]
    public void Constructor_WithNullUri_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new TransportAddress((Uri)null!));
        Assert.Equal("uri", exception.ParamName);
    }

    [Fact]
    public void Queue_FactoryMethod_CreatesQueueAddress()
    {
        // Arrange & Act
        var address = TransportAddress.Queue("test-queue");

        // Assert
        Assert.Equal("test-queue", address.Name);
        Assert.Equal(TransportAddressType.Queue, address.Type);
    }

    [Fact]
    public void Topic_FactoryMethod_CreatesTopicAddress()
    {
        // Arrange & Act
        var address = TransportAddress.Topic("test-topic");

        // Assert
        Assert.Equal("test-topic", address.Name);
        Assert.Equal(TransportAddressType.Topic, address.Type);
    }

    [Fact]
    public void Exchange_FactoryMethod_CreatesExchangeAddress()
    {
        // Arrange & Act
        var address = TransportAddress.Exchange("test-exchange");

        // Assert
        Assert.Equal("test-exchange", address.Name);
        Assert.Equal(TransportAddressType.Exchange, address.Type);
    }

    [Fact]
    public void Subscription_FactoryMethod_CreatesSubscriptionAddress()
    {
        // Arrange & Act
        var address = TransportAddress.Subscription("my-topic", "my-sub");

        // Assert
        Assert.Equal("my-topic/subscriptions/my-sub", address.Name);
        Assert.Equal(TransportAddressType.Subscription, address.Type);
        Assert.Equal("my-topic/subscriptions/my-sub", address.Path);
    }

    [Fact]
    public void Parse_WithSimpleName_CreatesQueue()
    {
        // Arrange & Act
        var address = TransportAddress.Parse("my-queue");

        // Assert
        Assert.Equal("my-queue", address.Name);
        Assert.Equal(TransportAddressType.Queue, address.Type);
    }

    [Fact]
    public void Parse_WithUri_ParsesCorrectly()
    {
        // Arrange & Act
        var address = TransportAddress.Parse("amqp://localhost/queues/my-queue");

        // Assert
        Assert.Equal("my-queue", address.Name);
        Assert.Equal("amqp", address.Scheme);
        Assert.Equal("localhost", address.Host);
    }

    [Fact]
    public void Parse_WithNullOrWhitespace_ThrowsArgumentException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentException>(() => TransportAddress.Parse(null!));
        Assert.Throws<ArgumentException>(() => TransportAddress.Parse(""));
        Assert.Throws<ArgumentException>(() => TransportAddress.Parse("   "));
    }

    [Fact]
    public void Parse_WithTopicsPath_ParsesAsTopicType()
    {
        // Arrange & Act
        var address = TransportAddress.Parse("hero://localhost/topics/my-topic");

        // Assert
        Assert.Equal("my-topic", address.Name);
        Assert.Equal(TransportAddressType.Topic, address.Type);
    }

    [Fact]
    public void ToString_WithSimpleQueue_ReturnsQueuePrefix()
    {
        // Arrange
        var address = new TransportAddress("my-queue");

        // Act
        var result = address.ToString();

        // Assert
        Assert.Equal("queue:my-queue", result);
    }

    [Fact]
    public void ToString_WithTopic_ReturnsTopicPrefix()
    {
        // Arrange
        var address = new TransportAddress("my-topic", TransportAddressType.Topic);

        // Act
        var result = address.ToString();

        // Assert
        Assert.Equal("topic:my-topic", result);
    }

    [Fact]
    public void ToString_WithExchange_ReturnsExchangePrefix()
    {
        // Arrange
        var address = new TransportAddress("my-exchange", TransportAddressType.Exchange);

        // Act
        var result = address.ToString();

        // Assert
        Assert.Equal("exchange:my-exchange", result);
    }

    [Fact]
    public void ToString_WithFullUri_ReturnsUriString()
    {
        // Arrange
        var address = new TransportAddress("my-queue")
        {
            Scheme = "rabbitmq",
            Host = "localhost",
            Port = 5672
        };

        // Act
        var result = address.ToString();

        // Assert
        Assert.Contains("rabbitmq://", result);
        Assert.Contains("localhost", result);
    }

    [Fact]
    public void ToUri_CreatesValidUri()
    {
        // Arrange
        var address = new TransportAddress("my-queue");

        // Act
        var uri = address.ToUri();

        // Assert
        Assert.NotNull(uri);
        Assert.Equal("hero", uri.Scheme);
        Assert.Equal("localhost", uri.Host);
    }

    [Fact]
    public void ToUri_WithCustomSchemeAndHost_UsesThoseValues()
    {
        // Arrange
        var address = new TransportAddress("my-queue")
        {
            Scheme = "amqp",
            Host = "server.example.com",
            Port = 5672
        };

        // Act
        var uri = address.ToUri();

        // Assert
        Assert.Equal("amqp", uri.Scheme);
        Assert.Equal("server.example.com", uri.Host);
        Assert.Equal(5672, uri.Port);
    }

    [Fact]
    public void Equality_WithSameValues_ReturnsTrue()
    {
        // Arrange
        var address1 = new TransportAddress("my-queue");
        var address2 = new TransportAddress("my-queue");

        // Act & Assert
        Assert.Equal(address1, address2);
        Assert.True(address1 == address2);
    }

    [Fact]
    public void Inequality_WithDifferentNames_ReturnsTrue()
    {
        // Arrange
        var address1 = new TransportAddress("queue1");
        var address2 = new TransportAddress("queue2");

        // Act & Assert
        Assert.NotEqual(address1, address2);
        Assert.True(address1 != address2);
    }

    [Fact]
    public void WithExpression_CreatesNewInstance()
    {
        // Arrange
        var original = new TransportAddress("original-queue");

        // Act
        var modified = original with { Name = "modified-queue" };

        // Assert
        Assert.Equal("original-queue", original.Name);
        Assert.Equal("modified-queue", modified.Name);
    }

    [Fact]
    public void VirtualHost_CanBeSet()
    {
        // Arrange & Act
        var address = new TransportAddress("my-queue")
        {
            VirtualHost = "/vhost"
        };

        // Assert
        Assert.Equal("/vhost", address.VirtualHost);
    }

    [Fact]
    public void Path_CanBeSet()
    {
        // Arrange & Act
        var address = new TransportAddress("my-queue")
        {
            Path = "custom/path/to/queue"
        };

        // Assert
        Assert.Equal("custom/path/to/queue", address.Path);
    }

    [Fact]
    public void Constructor_WithUriContainingPort_ParsesPort()
    {
        // Arrange
        var uri = new Uri("amqp://localhost:5672/queues/test");

        // Act
        var address = new TransportAddress(uri);

        // Assert
        Assert.Equal(5672, address.Port);
    }

    [Fact]
    public void Constructor_WithUriWithoutPort_SetsPortToNull()
    {
        // Arrange
        var uri = new Uri("amqp://localhost/queues/test");

        // Act
        var address = new TransportAddress(uri);

        // Assert
        Assert.Null(address.Port);
    }

    [Fact]
    public void Constructor_WithUriContainingExchanges_ParsesAsExchangeType()
    {
        // Arrange
        var uri = new Uri("rabbitmq://localhost/exchanges/my-exchange");

        // Act
        var address = new TransportAddress(uri);

        // Assert
        Assert.Equal(TransportAddressType.Exchange, address.Type);
        Assert.Equal("my-exchange", address.Name);
    }

    [Fact]
    public void Constructor_WithUriContainingSubscriptions_ParsesAsSubscriptionType()
    {
        // Arrange
        var uri = new Uri("asb://namespace.servicebus.windows.net/subscriptions/my-sub");

        // Act
        var address = new TransportAddress(uri);

        // Assert
        Assert.Equal(TransportAddressType.Subscription, address.Type);
        Assert.Equal("my-sub", address.Name);
    }

    [Fact]
    public void Constructor_WithEmptyPath_CreatesEmptyName()
    {
        // Arrange
        var uri = new Uri("hero://localhost");

        // Act
        var address = new TransportAddress(uri);

        // Assert
        Assert.Equal(string.Empty, address.Name);
        Assert.Equal(TransportAddressType.Queue, address.Type);
    }

    [Fact]
    public void Parse_CaseVariations_ParsesCorrectly()
    {
        // Arrange & Act
        var queue = TransportAddress.Parse("amqp://localhost/QUEUES/test");
        var topic = TransportAddress.Parse("amqp://localhost/TOPICS/test");

        // Assert
        Assert.Equal(TransportAddressType.Queue, queue.Type);
        Assert.Equal(TransportAddressType.Topic, topic.Type);
    }

    [Fact]
    public void Subscription_ToString_IncludesSubscriptionPath()
    {
        // Arrange
        var address = TransportAddress.Subscription("my-topic", "my-sub");

        // Act
        var result = address.ToString();

        // Assert
        Assert.Equal("subscription:my-topic/subscriptions/my-sub", result);
    }

    [Fact]
    public void ComplexUri_ParsesAllComponents()
    {
        // Arrange
        var uri = new Uri("rabbitmq://user:pass@server.com:5672/queues/my-queue?param=value");

        // Act
        var address = new TransportAddress(uri);

        // Assert
        Assert.Equal("rabbitmq", address.Scheme);
        Assert.Equal("server.com", address.Host);
        Assert.Equal(5672, address.Port);
        Assert.Equal("my-queue", address.Name);
    }
}
