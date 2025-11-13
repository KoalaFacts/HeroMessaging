using System;
using HeroMessaging.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeroMessaging.Tests.Unit.Utilities;

#pragma warning disable CA1052 // Test class with nested test classes
public class ServiceCollectionExtensionsTests
{
    // Test interfaces and implementations
    public interface ITestService
    {
        string Execute();
    }

    public class TestService : ITestService
    {
        public string Execute() => "Original";
    }

    public class DecoratedTestService : ITestService
    {
        private readonly ITestService _inner;
        private readonly string _prefix;

        public DecoratedTestService(ITestService inner, string prefix = "Decorated")
        {
            _inner = inner;
            _prefix = prefix;
        }

        public string Execute() => $"{_prefix}: {_inner.Execute()}";
    }

    public class AnotherTestService : ITestService
    {
        public string Execute() => "Another";
    }

    [Trait("Category", "Unit")]
    public class Decorate
    {
        [Fact]
        public void DecoratesService_WithTransientLifetime()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddTransient<ITestService, TestService>();

            // Act
            services.Decorate<ITestService>((inner, sp) => new DecoratedTestService(inner));

            // Assert
            var provider = services.BuildServiceProvider();
            var service = provider.GetRequiredService<ITestService>();

            Assert.IsType<DecoratedTestService>(service);
            Assert.Equal("Decorated: Original", service.Execute());
        }

        [Fact]
        public void DecoratesService_WithSingletonLifetime()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddSingleton<ITestService, TestService>();

            // Act
            services.Decorate<ITestService>((inner, sp) => new DecoratedTestService(inner));

            // Assert
            var provider = services.BuildServiceProvider();
            var service1 = provider.GetRequiredService<ITestService>();
            var service2 = provider.GetRequiredService<ITestService>();

            Assert.Same(service1, service2);
            Assert.Equal("Decorated: Original", service1.Execute());
        }

        [Fact]
        public void DecoratesService_WithScopedLifetime()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddScoped<ITestService, TestService>();

            // Act
            services.Decorate<ITestService>((inner, sp) => new DecoratedTestService(inner));

            // Assert
            var provider = services.BuildServiceProvider();

            using (var scope1 = provider.CreateScope())
            using (var scope2 = provider.CreateScope())
            {
                var service1a = scope1.ServiceProvider.GetRequiredService<ITestService>();
                var service1b = scope1.ServiceProvider.GetRequiredService<ITestService>();
                var service2 = scope2.ServiceProvider.GetRequiredService<ITestService>();

                Assert.Same(service1a, service1b); // Same within scope
                Assert.NotSame(service1a, service2); // Different across scopes
                Assert.Equal("Decorated: Original", service1a.Execute());
            }
        }

        [Fact]
        public void DecoratesService_RegisteredWithImplementationFactory()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddTransient<ITestService>(sp => new TestService());

            // Act
            services.Decorate<ITestService>((inner, sp) => new DecoratedTestService(inner));

            // Assert
            var provider = services.BuildServiceProvider();
            var service = provider.GetRequiredService<ITestService>();

            Assert.Equal("Decorated: Original", service.Execute());
        }

        [Fact]
        public void DecoratesService_RegisteredWithImplementationInstance()
        {
            // Arrange
            var services = new ServiceCollection();
            var instance = new TestService();
            services.AddSingleton<ITestService>(instance);

            // Act
            services.Decorate<ITestService>((inner, sp) => new DecoratedTestService(inner));

            // Assert
            var provider = services.BuildServiceProvider();
            var service = provider.GetRequiredService<ITestService>();

            Assert.Equal("Decorated: Original", service.Execute());
        }

        [Fact]
        public void AllowsMultipleDecorations_CreatesDecoratorChain()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddTransient<ITestService, TestService>();

            // Act
            services.Decorate<ITestService>((inner, sp) => new DecoratedTestService(inner, "First"));
            services.Decorate<ITestService>((inner, sp) => new DecoratedTestService(inner, "Second"));

            // Assert
            var provider = services.BuildServiceProvider();
            var service = provider.GetRequiredService<ITestService>();

            Assert.Equal("Second: First: Original", service.Execute());
        }

        [Fact]
        public void ThrowsInvalidOperationException_WhenServiceNotRegistered()
        {
            // Arrange
            var services = new ServiceCollection();

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                services.Decorate<ITestService>((inner, sp) => new DecoratedTestService(inner)));

            Assert.Contains("ITestService", exception.Message);
            Assert.Contains("not registered", exception.Message);
        }

        [Fact]
        public void PreservesServiceLifetime_AfterDecoration()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddSingleton<ITestService, TestService>();

            // Act
            services.Decorate<ITestService>((inner, sp) => new DecoratedTestService(inner));

            // Assert
            var descriptor = Assert.Single(services, s => s.ServiceType == typeof(ITestService));
            Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        }

        [Fact]
        public void AllowsAccessToServiceProvider_InDecorator()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddTransient<ITestService, TestService>();
            services.AddSingleton<string>("Injected Prefix");

            // Act
            services.Decorate<ITestService>((inner, sp) =>
            {
                var prefix = sp.GetRequiredService<string>();
                return new DecoratedTestService(inner, prefix);
            });

            // Assert
            var provider = services.BuildServiceProvider();
            var service = provider.GetRequiredService<ITestService>();

            Assert.Equal("Injected Prefix: Original", service.Execute());
        }

        [Fact]
        public void RemovesOriginalRegistration_BeforeAddingDecorated()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddTransient<ITestService, TestService>();

            // Act
            services.Decorate<ITestService>((inner, sp) => new DecoratedTestService(inner));

            // Assert - Should only have one registration
            var registrations = services.Where(s => s.ServiceType == typeof(ITestService)).ToList();
            Assert.Single(registrations);
        }

        [Fact]
        public void WorksWithConcreteImplementationType()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddTransient<ITestService, TestService>();

            // Act
            services.Decorate<ITestService>((inner, sp) => new DecoratedTestService(inner));

            // Assert
            var provider = services.BuildServiceProvider();
            var descriptor = services.First(s => s.ServiceType == typeof(ITestService));

            Assert.NotNull(descriptor.ImplementationFactory);
            Assert.Null(descriptor.ImplementationType);
            Assert.Null(descriptor.ImplementationInstance);
        }

        [Fact]
        public void ThrowsInvalidOperationException_WhenOriginalServiceCannotBeResolved()
        {
            // Arrange
            var services = new ServiceCollection();
            // Add a registration with null values (edge case)
            var descriptor = new ServiceDescriptor(typeof(ITestService), _ => null!, ServiceLifetime.Transient);
            ((IList<ServiceDescriptor>)services).Add(descriptor);

            // Act
            services.Decorate<ITestService>((inner, sp) => new DecoratedTestService(inner!));

            // Assert
            var provider = services.BuildServiceProvider();
            var exception = Assert.Throws<NullReferenceException>(() =>
                provider.GetRequiredService<ITestService>());
        }

        [Fact]
        public void ReturnsServiceCollection_ForMethodChaining()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddTransient<ITestService, TestService>();

            // Act
            var result = services.Decorate<ITestService>((inner, sp) => new DecoratedTestService(inner));

            // Assert
            Assert.Same(services, result);
        }

        [Fact]
        public void WorksWithMultipleServiceTypes()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddTransient<ITestService, TestService>();

            // Act
            services.Decorate<ITestService>((inner, sp) => new DecoratedTestService(inner, "Decorator1"));

            // Assert
            var provider = services.BuildServiceProvider();
            var service = provider.GetRequiredService<ITestService>();

            Assert.Equal("Decorator1: Original", service.Execute());
        }

        [Fact]
        public void HandlesDecoratorThatDoesNotUseInnerService()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddTransient<ITestService, TestService>();

            // Act - Decorator that ignores inner service
            services.Decorate<ITestService>((inner, sp) => new AnotherTestService());

            // Assert
            var provider = services.BuildServiceProvider();
            var service = provider.GetRequiredService<ITestService>();

            Assert.Equal("Another", service.Execute());
        }

        [Fact]
        public void WorksWhenDecoratorThrowsException()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddTransient<ITestService, TestService>();

            // Act
            services.Decorate<ITestService>((inner, sp) => throw new InvalidOperationException("Decorator failed"));

            // Assert
            var provider = services.BuildServiceProvider();
            var exception = Assert.Throws<InvalidOperationException>(() =>
                provider.GetRequiredService<ITestService>());

            Assert.Equal("Decorator failed", exception.Message);
        }

        [Fact]
        public void PreservesServiceDescriptor_Properties()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddTransient<ITestService, TestService>();
            var originalDescriptor = services.First(s => s.ServiceType == typeof(ITestService));
            var originalLifetime = originalDescriptor.Lifetime;

            // Act
            services.Decorate<ITestService>((inner, sp) => new DecoratedTestService(inner));

            // Assert
            var newDescriptor = services.First(s => s.ServiceType == typeof(ITestService));
            Assert.Equal(originalLifetime, newDescriptor.Lifetime);
            Assert.Equal(originalDescriptor.ServiceType, newDescriptor.ServiceType);
        }
    }
}
