using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Abstractions.Queries;
using HeroMessaging.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HeroMessaging.Examples;

// Example 1: Basic Setup with Minimal Configuration
public class BasicSetupExample
{
    public static void ConfigureServices(IServiceCollection services)
    {
        // Simple development setup with in-memory storage
        services.AddHeroMessaging(builder => builder
            .Development()
            .ScanAssembly(typeof(BasicSetupExample).Assembly)
        );
    }
}

// Example 2: Production Setup with All Features
public class ProductionSetupExample
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddHeroMessaging(builder => builder
            .WithMediator()           // Enable command/query processing
            .WithEventBus()          // Enable event publishing
            .WithQueues()            // Enable queue processing
            .WithOutbox()            // Enable outbox pattern
            .WithInbox()             // Enable inbox pattern
            .WithErrorHandling()     // Enable dead letter queue
            .UseInMemoryStorage()    // Or use SQL/PostgreSQL storage
            .ScanAssembly(typeof(ProductionSetupExample).Assembly)
            .ConfigureProcessing(options =>
            {
                options.MaxConcurrency = 10;
                options.MaxRetries = 3;
                options.EnableCircuitBreaker = true;
            })
        );
    }
}

// Example 3: Microservice Setup
public class MicroserviceSetupExample
{
    public static void ConfigureServices(IServiceCollection services)
    {
        var connectionString = "Server=localhost;Database=HeroMessaging;";
        
        services.AddHeroMessaging(builder => builder
            .Microservice(connectionString)  // Pre-configured for microservices
            .WithErrorHandling()
            .ScanAssembly(typeof(MicroserviceSetupExample).Assembly)
        );
    }
}

// Example 4: ASP.NET Core Integration
// Note: In ASP.NET Core projects, use the standard AddHeroMessaging extension in Program.cs:
// builder.Services.AddHeroMessaging(messaging => messaging
//     .WithMediator()
//     .WithEventBus()
//     .UseInMemoryStorage()
//     .ScanAssembly(typeof(Program).Assembly)
// );

// Example 5: Console Application with Host Builder
public class ConsoleAppExample
{
    public static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .UseHeroMessaging(builder => builder
                .WithMediator()
                .WithEventBus()
                .WithQueues()
                .UseInMemoryStorage()
                .ScanAssembly(typeof(ConsoleAppExample).Assembly)
            )
            .Build();
        
        // Get the messaging service
        var messaging = host.Services.GetRequiredService<IHeroMessaging>();
        
        // Send a command
        await messaging.Send(new ProcessOrderCommand { OrderId = 123 });
        
        // Send a query
        var order = await messaging.Send<OrderDto>(new GetOrderQuery { OrderId = 123 });
        
        // Publish an event
        await messaging.Publish(new OrderProcessedEvent { OrderId = 123 });
        
        // Enqueue a message
        await messaging.Enqueue(
            new ProcessPaymentMessage { Amount = 99.99m }, 
            "payment-queue",
            new EnqueueOptions { Priority = 1 });
        
        await host.RunAsync();
    }
}

// Example 6: Using Different Storage Providers
public class StorageConfigurationExample
{
    public static void ConfigureWithSqlServer(IServiceCollection services)
    {
        services.AddHeroMessaging(builder => builder
            .WithMediator()
            .WithEventBus()
            .WithOutbox()
            // SQL Server storage would be configured here
            // .UseSqlServerStorage("connection-string")
            .ScanAssembly(typeof(StorageConfigurationExample).Assembly)
        );
    }
    
    public static void ConfigureWithPostgreSQL(IServiceCollection services)
    {
        services.AddHeroMessaging(builder => builder
            .WithMediator()
            .WithEventBus()
            .WithOutbox()
            // PostgreSQL storage would be configured here
            // .UsePostgreSQLStorage("connection-string")
            .ScanAssembly(typeof(StorageConfigurationExample).Assembly)
        );
    }
}

// Example Commands, Queries, and Events
public class CreateUserCommand : ICommand
{
    public string Name { get; set; } = string.Empty;
}

public class GetUserQuery : IQuery<UserDto>
{
    public int Id { get; set; }
}

public class UserCreatedEvent : IEvent
{
    public int UserId { get; set; }
}

public class ProcessOrderCommand : ICommand
{
    public int OrderId { get; set; }
}

public class GetOrderQuery : IQuery<OrderDto>
{
    public int OrderId { get; set; }
}

public class OrderProcessedEvent : IEvent
{
    public int OrderId { get; set; }
}

public class ProcessPaymentMessage : IMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public decimal Amount { get; set; }
}

// Example DTOs
public class UserDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class OrderDto
{
    public int Id { get; set; }
    public decimal Total { get; set; }
}

// Example Handlers
public class CreateUserCommandHandler : ICommandHandler<CreateUserCommand>
{
    public Task Handle(CreateUserCommand command, CancellationToken cancellationToken)
    {
        // Handle command logic
        Console.WriteLine($"Creating user: {command.Name}");
        return Task.CompletedTask;
    }
}

public class GetUserQueryHandler : IQueryHandler<GetUserQuery, UserDto>
{
    public Task<UserDto> Handle(GetUserQuery query, CancellationToken cancellationToken)
    {
        // Handle query logic
        return Task.FromResult(new UserDto { Id = query.Id, Name = "John Doe" });
    }
}

public class UserCreatedEventHandler : IEventHandler<UserCreatedEvent>
{
    public Task Handle(UserCreatedEvent @event, CancellationToken cancellationToken)
    {
        // Handle event logic
        Console.WriteLine($"User created with ID: {@event.UserId}");
        return Task.CompletedTask;
    }
}