using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Storage.SqlServer;
using HeroMessaging.Tests.Integration.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace HeroMessaging.Tests.Integration.Storage;

public class SqlServerOutboxStorageTests : DatabaseIntegrationTestBase
{
    private IOutboxStorage _outboxStorage = null!;

    public SqlServerOutboxStorageTests() : base(DatabaseProvider.SqlServer)
    {
    }

    protected override async Task ConfigureServicesAsync(IServiceCollection services)
    {
        services.AddSingleton<IOutboxStorage>(new SqlServerOutboxStorage(ConnectionString));
        await base.ConfigureServicesAsync(services);
    }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        _outboxStorage = GetRequiredService<IOutboxStorage>();
    }

    protected override async Task InitializeDatabaseSchemaAsync()
    {
        using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        var createTableScript = @"
            IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='outbox_messages' AND xtype='U')
            CREATE TABLE outbox_messages (
                id NVARCHAR(450) PRIMARY KEY,
                message_id NVARCHAR(450) NOT NULL,
                message_type NVARCHAR(500) NOT NULL,
                message_data NVARCHAR(MAX) NOT NULL,
                destination NVARCHAR(500),
                priority INT NOT NULL DEFAULT 0,
                max_retries INT NOT NULL DEFAULT 3,
                retry_count INT NOT NULL DEFAULT 0,
                created_at DATETIME2 NOT NULL,
                processed_at DATETIME2,
                next_retry_at DATETIME2,
                last_error NVARCHAR(MAX),
                status NVARCHAR(50) NOT NULL
            );";

        using var command = new SqlCommand(createTableScript, connection);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Should_Add_And_Retrieve_Outbox_Entry()
    {
        // Arrange
        var message = new TestMessage { Id = Guid.NewGuid(), Data = "Test Data" };
        var options = new OutboxOptions { Destination = "TestDestination", Priority = 1 };

        // Act
        var entry = await _outboxStorage.Add(message, options);
        var pending = await _outboxStorage.GetPending(10);

        // Assert
        Assert.NotNull(entry);
        Assert.NotEmpty(entry.Id);
        Assert.Single(pending, e => e.Id == entry.Id);
        
        var retrievedEntry = pending.First(e => e.Id == entry.Id);
        Assert.Equal(options.Destination, retrievedEntry.Options.Destination);
        Assert.Equal(options.Priority, retrievedEntry.Options.Priority);
        Assert.Equal(OutboxStatus.Pending, retrievedEntry.Status);
    }

    [Fact]
    public async Task Should_Mark_Entry_As_Processed()
    {
        // Arrange
        var message = new TestMessage { Id = Guid.NewGuid(), Data = "Test Data" };
        var options = new OutboxOptions();
        var entry = await _outboxStorage.Add(message, options);

        // Act
        var result = await _outboxStorage.MarkProcessed(entry.Id);
        var pending = await _outboxStorage.GetPending(10);

        // Assert
        Assert.True(result);
        Assert.DoesNotContain(pending, e => e.Id == entry.Id);
    }

    [Fact]
    public async Task Should_Mark_Entry_As_Failed()
    {
        // Arrange
        var message = new TestMessage { Id = Guid.NewGuid(), Data = "Test Data" };
        var options = new OutboxOptions();
        var entry = await _outboxStorage.Add(message, options);
        var errorMessage = "Test error";

        // Act
        var result = await _outboxStorage.MarkFailed(entry.Id, errorMessage);
        var failed = await _outboxStorage.GetFailed(10);

        // Assert
        Assert.True(result);
        Assert.Single(failed, e => e.Id == entry.Id);
        
        var failedEntry = failed.First(e => e.Id == entry.Id);
        Assert.Equal(errorMessage, failedEntry.LastError);
        Assert.Equal(OutboxStatus.Failed, failedEntry.Status);
    }

    [Fact]
    public async Task Should_Get_Pending_Count()
    {
        // Arrange
        var message1 = new TestMessage { Id = Guid.NewGuid(), Data = "Data 1" };
        var message2 = new TestMessage { Id = Guid.NewGuid(), Data = "Data 2" };
        var options = new OutboxOptions();

        // Act
        await _outboxStorage.Add(message1, options);
        await _outboxStorage.Add(message2, options);
        var count = await _outboxStorage.GetPendingCount();

        // Assert
        Assert.True(count >= 2);
    }
}

public class TestMessage : IEvent
{
    public Guid Id { get; set; }
    public string Data { get; set; } = string.Empty;
    public Guid MessageId { get; } = Guid.NewGuid();
    public DateTime Timestamp { get; } = DateTime.UtcNow;
    public Dictionary<string, object>? Metadata { get; set; }
}