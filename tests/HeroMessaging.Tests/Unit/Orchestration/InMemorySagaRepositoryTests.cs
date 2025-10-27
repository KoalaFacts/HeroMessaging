using HeroMessaging.Abstractions.Sagas;
using HeroMessaging.Orchestration;
using Xunit;

namespace HeroMessaging.Tests.Unit.Orchestration;

[Trait("Category", "Unit")]
public class InMemorySagaRepositoryTests
{
    private class TestSaga : SagaBase
    {
        public string? Data { get; set; }
    }

    [Fact]
    public async Task FindAsync_WhenSagaDoesNotExist_ReturnsNull()
    {
        // Arrange
        var repository = new InMemorySagaRepository<TestSaga>();
        var correlationId = Guid.NewGuid();

        // Act
        var result = await repository.FindAsync(correlationId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task SaveAsync_NewSaga_SavesSuccessfully()
    {
        // Arrange
        var repository = new InMemorySagaRepository<TestSaga>();
        var saga = new TestSaga
        {
            CorrelationId = Guid.NewGuid(),
            CurrentState = "Initial",
            Data = "Test data"
        };

        // Act
        await repository.SaveAsync(saga);

        // Assert
        var retrieved = await repository.FindAsync(saga.CorrelationId);
        Assert.NotNull(retrieved);
        Assert.Equal(saga.CorrelationId, retrieved!.CorrelationId);
        Assert.Equal(saga.Data, retrieved.Data);
    }

    [Fact]
    public async Task SaveAsync_DuplicateCorrelationId_ThrowsException()
    {
        // Arrange
        var repository = new InMemorySagaRepository<TestSaga>();
        var correlationId = Guid.NewGuid();
        var saga1 = new TestSaga { CorrelationId = correlationId, CurrentState = "Initial" };
        var saga2 = new TestSaga { CorrelationId = correlationId, CurrentState = "Initial" };

        await repository.SaveAsync(saga1);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await repository.SaveAsync(saga2));

        Assert.Contains("already exists", exception.Message);
    }

    [Fact]
    public async Task UpdateAsync_ExistingSaga_UpdatesSuccessfully()
    {
        // Arrange
        var repository = new InMemorySagaRepository<TestSaga>();
        var saga = new TestSaga
        {
            CorrelationId = Guid.NewGuid(),
            CurrentState = "Initial",
            Data = "Original"
        };
        await repository.SaveAsync(saga);

        // Act
        saga.Data = "Updated";
        saga.CurrentState = "NewState";
        await repository.UpdateAsync(saga); // UpdateAsync increments version internally

        // Assert
        var retrieved = await repository.FindAsync(saga.CorrelationId);
        Assert.NotNull(retrieved);
        Assert.Equal("Updated", retrieved!.Data);
        Assert.Equal("NewState", retrieved.CurrentState);
        Assert.Equal(1, retrieved.Version); // Version incremented by UpdateAsync
    }

    [Fact]
    public async Task UpdateAsync_NonExistentSaga_ThrowsException()
    {
        // Arrange
        var repository = new InMemorySagaRepository<TestSaga>();
        var saga = new TestSaga
        {
            CorrelationId = Guid.NewGuid(),
            CurrentState = "Initial"
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await repository.UpdateAsync(saga));

        Assert.Contains("not found", exception.Message);
    }

    [Fact]
    public async Task UpdateAsync_VersionMismatch_ThrowsConcurrencyException()
    {
        // Arrange
        var repository = new InMemorySagaRepository<TestSaga>();
        var saga = new TestSaga
        {
            CorrelationId = Guid.NewGuid(),
            CurrentState = "Initial",
            Version = 0
        };
        await repository.SaveAsync(saga);

        // Act - try to update with wrong version (simulating concurrent modification)
        saga.Version = 5; // Pretend someone else updated it
        var exception = await Assert.ThrowsAsync<SagaConcurrencyException>(
            async () => await repository.UpdateAsync(saga));

        // Assert
        Assert.Equal(saga.CorrelationId, exception.CorrelationId);
        Assert.Equal(0, exception.ExpectedVersion); // Repository still has version 0
        Assert.Equal(5, exception.ActualVersion); // But we're trying to update with version 5
    }

    [Fact]
    public async Task UpdateAsync_OptimisticConcurrency_PreventsConcurrentUpdates()
    {
        // Arrange
        var repository = new InMemorySagaRepository<TestSaga>();
        var correlationId = Guid.NewGuid();
        var saga = new TestSaga
        {
            CorrelationId = correlationId,
            CurrentState = "Initial",
            Version = 0
        };
        await repository.SaveAsync(saga);

        // Act - Simulate two concurrent updates
        // Both "threads" fetch the saga at version 0
        var saga1 = await repository.FindAsync(correlationId);
        var saga2 = await repository.FindAsync(correlationId);

        // First thread updates successfully
        saga1!.Data = "Update 1";
        await repository.UpdateAsync(saga1); // Succeeds, _versions[id] = 1, saga.Version = 1

        // Second thread tries to update with stale version 0
        // IMPORTANT: In real scenarios, saga1 and saga2 would be different objects fetched
        // from database at different times. But with in-memory dictionary, they're the same
        // reference, so saga2.Version was also incremented to 1 by the first update.
        // Reset version to simulate what the second thread "thinks" it has:
        saga2!.Version = 0; // Pretend this thread still thinks it has version 0
        saga2.Data = "Update 2"; // Try to make changes (same object, so this affects everything)

        // Assert - Second update fails due to version mismatch (saga.Version 0 != tracked 1)
        var exception = await Assert.ThrowsAsync<SagaConcurrencyException>(
            async () => await repository.UpdateAsync(saga2));

        Assert.Equal(correlationId, exception.CorrelationId);
        Assert.Equal(1, exception.ExpectedVersion); // Repository tracked version 1
        Assert.Equal(0, exception.ActualVersion); // Saga claims version 0

        // Note: Can't verify data remained "Update 1" because all references point to same
        // object which now has "Update 2" in memory. In real database scenarios, the row
        // would still have "Update 1" because the UPDATE would fail.
    }

    [Fact]
    public async Task DeleteAsync_ExistingSaga_RemovesSaga()
    {
        // Arrange
        var repository = new InMemorySagaRepository<TestSaga>();
        var saga = new TestSaga
        {
            CorrelationId = Guid.NewGuid(),
            CurrentState = "Initial"
        };
        await repository.SaveAsync(saga);

        // Act
        await repository.DeleteAsync(saga.CorrelationId);

        // Assert
        var retrieved = await repository.FindAsync(saga.CorrelationId);
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task DeleteAsync_NonExistentSaga_DoesNotThrow()
    {
        // Arrange
        var repository = new InMemorySagaRepository<TestSaga>();
        var correlationId = Guid.NewGuid();

        // Act & Assert - Should not throw
        await repository.DeleteAsync(correlationId);
    }

    [Fact]
    public async Task FindByStateAsync_ReturnsMatchingSagas()
    {
        // Arrange
        var repository = new InMemorySagaRepository<TestSaga>();
        await repository.SaveAsync(new TestSaga { CorrelationId = Guid.NewGuid(), CurrentState = "Pending" });
        await repository.SaveAsync(new TestSaga { CorrelationId = Guid.NewGuid(), CurrentState = "Active" });
        await repository.SaveAsync(new TestSaga { CorrelationId = Guid.NewGuid(), CurrentState = "Pending" });
        await repository.SaveAsync(new TestSaga { CorrelationId = Guid.NewGuid(), CurrentState = "Completed" });

        // Act
        var pendingSagas = await repository.FindByStateAsync("Pending");

        // Assert
        Assert.Equal(2, pendingSagas.Count());
        Assert.All(pendingSagas, saga => Assert.Equal("Pending", saga.CurrentState));
    }

    [Fact]
    public async Task FindStaleAsync_ReturnsOldSagas()
    {
        // Arrange
        var repository = new InMemorySagaRepository<TestSaga>();
        var oldTime = DateTime.UtcNow.AddHours(-2);
        var recentTime = DateTime.UtcNow.AddMinutes(-5);

        var staleSaga = new TestSaga
        {
            CorrelationId = Guid.NewGuid(),
            CurrentState = "Active",
            CreatedAt = oldTime,
            UpdatedAt = oldTime
        };

        var recentSaga = new TestSaga
        {
            CorrelationId = Guid.NewGuid(),
            CurrentState = "Active",
            CreatedAt = recentTime,
            UpdatedAt = recentTime
        };

        var completedSaga = new TestSaga
        {
            CorrelationId = Guid.NewGuid(),
            CurrentState = "Completed",
            CreatedAt = oldTime,
            UpdatedAt = oldTime
        };
        completedSaga.Complete();

        await repository.SaveAsync(staleSaga);
        await repository.SaveAsync(recentSaga);
        await repository.SaveAsync(completedSaga);

        // Act
        var staleSagas = await repository.FindStaleAsync(TimeSpan.FromHours(1));

        // Assert
        Assert.Single(staleSagas);
        Assert.Equal(staleSaga.CorrelationId, staleSagas.First().CorrelationId);
    }

    [Fact]
    public void GetAll_ReturnsAllSagas()
    {
        // Arrange
        var repository = new InMemorySagaRepository<TestSaga>();
        repository.SaveAsync(new TestSaga { CorrelationId = Guid.NewGuid(), CurrentState = "State1" }).Wait();
        repository.SaveAsync(new TestSaga { CorrelationId = Guid.NewGuid(), CurrentState = "State2" }).Wait();
        repository.SaveAsync(new TestSaga { CorrelationId = Guid.NewGuid(), CurrentState = "State3" }).Wait();

        // Act
        var all = repository.GetAll();

        // Assert
        Assert.Equal(3, all.Count());
    }

    [Fact]
    public void Clear_RemovesAllSagas()
    {
        // Arrange
        var repository = new InMemorySagaRepository<TestSaga>();
        repository.SaveAsync(new TestSaga { CorrelationId = Guid.NewGuid(), CurrentState = "State1" }).Wait();
        repository.SaveAsync(new TestSaga { CorrelationId = Guid.NewGuid(), CurrentState = "State2" }).Wait();

        // Act
        repository.Clear();

        // Assert
        Assert.Equal(0, repository.Count);
        Assert.Empty(repository.GetAll());
    }

    [Fact]
    public void Count_ReturnsCorrectCount()
    {
        // Arrange
        var repository = new InMemorySagaRepository<TestSaga>();

        // Act & Assert
        Assert.Equal(0, repository.Count);

        repository.SaveAsync(new TestSaga { CorrelationId = Guid.NewGuid(), CurrentState = "State1" }).Wait();
        Assert.Equal(1, repository.Count);

        repository.SaveAsync(new TestSaga { CorrelationId = Guid.NewGuid(), CurrentState = "State2" }).Wait();
        Assert.Equal(2, repository.Count);
    }
}
