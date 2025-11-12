using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Transport.InMemory;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeroMessaging.Tests.Unit.Transport
{
    [Trait("Category", "Unit")]
    public sealed class InMemoryTransportExtensionsTests
    {
        #region AddInMemoryTransport (without name) Tests

        [Fact]
        public void AddInMemoryTransport_WithNullServices_ThrowsArgumentNullException()
        {
            // Arrange
            IServiceCollection services = null!;

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                services.AddInMemoryTransport());
        }

        [Fact]
        public void AddInMemoryTransport_WithoutConfigure_RegistersTransport()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddSingleton(TimeProvider.System);

            // Act
            var result = services.AddInMemoryTransport();

            // Assert
            Assert.Same(services, result);
            var serviceProvider = services.BuildServiceProvider();
            var transport = serviceProvider.GetService<IMessageTransport>();
            Assert.NotNull(transport);
        }

        [Fact]
        public void AddInMemoryTransport_WithConfigure_AppliesConfiguration()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddSingleton(TimeProvider.System);
            var configureWasCalled = false;

            // Act
            services.AddInMemoryTransport(options =>
            {
                configureWasCalled = true;
                options.MaxQueueLength = 5000;
            });

            // Assert
            Assert.True(configureWasCalled);
            var serviceProvider = services.BuildServiceProvider();
            var transport = serviceProvider.GetService<IMessageTransport>();
            Assert.NotNull(transport);
        }

        [Fact]
        public void AddInMemoryTransport_WithNullConfigure_DoesNotThrow()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddSingleton(TimeProvider.System);

            // Act
            services.AddInMemoryTransport(configure: null);

            // Assert
            var serviceProvider = services.BuildServiceProvider();
            var transport = serviceProvider.GetService<IMessageTransport>();
            Assert.NotNull(transport);
        }

        [Fact]
        public void AddInMemoryTransport_RegistersOptions()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddSingleton(TimeProvider.System);

            // Act
            services.AddInMemoryTransport(options =>
            {
                options.MaxQueueLength = 3000;
            });

            // Assert
            var serviceProvider = services.BuildServiceProvider();
            var options = serviceProvider.GetService<InMemoryTransportOptions>();
            Assert.NotNull(options);
            Assert.Equal(3000, options.MaxQueueLength);
        }

        #endregion

        #region AddInMemoryTransport (with name) Tests

        [Fact]
        public void AddInMemoryTransport_WithName_WithNullServices_ThrowsArgumentNullException()
        {
            // Arrange
            IServiceCollection services = null!;

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                services.AddInMemoryTransport("TestTransport"));
        }

        [Fact]
        public void AddInMemoryTransport_WithName_SetsNameInOptions()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddSingleton(TimeProvider.System);
            var transportName = "MyCustomTransport";

            // Act
            services.AddInMemoryTransport(transportName);

            // Assert
            var serviceProvider = services.BuildServiceProvider();
            var options = serviceProvider.GetService<InMemoryTransportOptions>();
            Assert.NotNull(options);
            Assert.Equal(transportName, options.Name);
        }

        [Fact]
        public void AddInMemoryTransport_WithName_AndConfigure_AppliesBoth()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddSingleton(TimeProvider.System);
            var transportName = "MyTransport";

            // Act
            services.AddInMemoryTransport(transportName, options =>
            {
                options.MaxQueueLength = 4000;
            });

            // Assert
            var serviceProvider = services.BuildServiceProvider();
            var options = serviceProvider.GetService<InMemoryTransportOptions>();
            Assert.NotNull(options);
            Assert.Equal(transportName, options.Name);
            Assert.Equal(4000, options.MaxQueueLength);
        }

        [Fact]
        public void AddInMemoryTransport_WithName_RegistersTransport()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddSingleton(TimeProvider.System);

            // Act
            services.AddInMemoryTransport("TestTransport");

            // Assert
            var serviceProvider = services.BuildServiceProvider();
            var transport = serviceProvider.GetService<IMessageTransport>();
            Assert.NotNull(transport);
        }

        [Fact]
        public void AddInMemoryTransport_WithName_WithNullConfigure_DoesNotThrow()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddSingleton(TimeProvider.System);

            // Act
            services.AddInMemoryTransport("TestTransport", configure: null);

            // Assert
            var serviceProvider = services.BuildServiceProvider();
            var transport = serviceProvider.GetService<IMessageTransport>();
            Assert.NotNull(transport);
        }

        #endregion

        #region Chaining Tests

        [Fact]
        public void AddInMemoryTransport_ReturnsServiceCollection_ForChaining()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddSingleton(TimeProvider.System);

            // Act
            var result = services
                .AddInMemoryTransport()
                .AddSingleton<string>("test");

            // Assert
            Assert.Same(services, result);
        }

        [Fact]
        public void AddInMemoryTransport_WithName_ReturnsServiceCollection_ForChaining()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddSingleton(TimeProvider.System);

            // Act
            var result = services
                .AddInMemoryTransport("Transport1")
                .AddSingleton<string>("test");

            // Assert
            Assert.Same(services, result);
        }

        #endregion
    }
}
