using HeroMessaging.Abstractions.Sagas;
using HeroMessaging.Orchestration;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace HeroMessaging.Tests.Unit.Orchestration
{
    [Trait("Category", "Unit")]
    public sealed class InMemorySagaRepositoryTests
    {
        private readonly FakeTimeProvider _timeProvider;

        public InMemorySagaRepositoryTests()
        {
            _timeProvider = new FakeTimeProvider();
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new InMemorySagaRepository<TestSaga>(null!));
            Assert.Equal("timeProvider", ex.ParamName);
        }

        [Fact]
        public void Constructor_WithValidTimeProvider_CreatesInstance()
        {
            // Act
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);

            // Assert
            Assert.NotNull(repository);
            Assert.Equal(0, repository.Count);
        }

        #endregion

        #region SaveAsync Tests

        [Fact]
        public async Task SaveAsync_WithNullSaga_ThrowsArgumentNullException()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);

            // Act & Assert
            var ex = await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await repository.SaveAsync(null!));
            Assert.Equal("saga", ex.ParamName);
        }

        [Fact]
        public async Task SaveAsync_WithNewSaga_SavesSuccessfully()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);
            var saga = new TestSaga { CorrelationId = Guid.NewGuid() };

            // Act
            await repository.SaveAsync(saga);

            // Assert
            Assert.Equal(1, repository.Count);
            var retrieved = await repository.FindAsync(saga.CorrelationId);
            Assert.NotNull(retrieved);
            Assert.Equal(saga.CorrelationId, retrieved.CorrelationId);
        }

        [Fact]
        public async Task SaveAsync_SetsCreatedAtTimestamp()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);
            var saga = new TestSaga { CorrelationId = Guid.NewGuid() };
            var expectedTime = _timeProvider.GetUtcNow();

            // Act
            await repository.SaveAsync(saga);

            // Assert
            Assert.Equal(expectedTime, saga.CreatedAt);
        }

        [Fact]
        public async Task SaveAsync_SetsUpdatedAtTimestamp()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);
            var saga = new TestSaga { CorrelationId = Guid.NewGuid() };
            var expectedTime = _timeProvider.GetUtcNow();

            // Act
            await repository.SaveAsync(saga);

            // Assert
            Assert.Equal(expectedTime, saga.UpdatedAt);
        }

        [Fact]
        public async Task SaveAsync_InitializesVersionToZero()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);
            var saga = new TestSaga { CorrelationId = Guid.NewGuid() };

            // Act
            await repository.SaveAsync(saga);

            // Assert
            Assert.Equal(0, saga.Version);
        }

        [Fact]
        public async Task SaveAsync_WithExistingCorrelationId_ThrowsInvalidOperationException()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);
            var correlationId = Guid.NewGuid();
            var saga1 = new TestSaga { CorrelationId = correlationId };
            var saga2 = new TestSaga { CorrelationId = correlationId };

            await repository.SaveAsync(saga1);

            // Act & Assert
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await repository.SaveAsync(saga2));
            Assert.Contains("already exists", ex.Message);
            Assert.Contains("UpdateAsync", ex.Message);
        }

        [Fact]
        public async Task SaveAsync_WithCancellation_ThrowsOperationCanceledException()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);
            var saga = new TestSaga { CorrelationId = Guid.NewGuid() };
            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await repository.SaveAsync(saga, cts.Token));
        }

        #endregion

        #region FindAsync Tests

        [Fact]
        public async Task FindAsync_WithNonExistentCorrelationId_ReturnsNull()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);

            // Act
            var result = await repository.FindAsync(Guid.NewGuid());

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task FindAsync_WithExistingCorrelationId_ReturnsSaga()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);
            var saga = new TestSaga { CorrelationId = Guid.NewGuid(), CurrentState = "TestState" };
            await repository.SaveAsync(saga);

            // Act
            var result = await repository.FindAsync(saga.CorrelationId);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(saga.CorrelationId, result.CorrelationId);
            Assert.Equal("TestState", result.CurrentState);
        }

        [Fact]
        public async Task FindAsync_WithCancellation_ThrowsOperationCanceledException()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);
            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await repository.FindAsync(Guid.NewGuid(), cts.Token));
        }

        #endregion

        #region UpdateAsync Tests

        [Fact]
        public async Task UpdateAsync_WithNullSaga_ThrowsArgumentNullException()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);

            // Act & Assert
            var ex = await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await repository.UpdateAsync(null!));
            Assert.Equal("saga", ex.ParamName);
        }

        [Fact]
        public async Task UpdateAsync_WithNonExistentSaga_ThrowsInvalidOperationException()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);
            var saga = new TestSaga { CorrelationId = Guid.NewGuid() };

            // Act & Assert
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await repository.UpdateAsync(saga));
            Assert.Contains("not found", ex.Message);
            Assert.Contains("SaveAsync", ex.Message);
        }

        [Fact]
        public async Task UpdateAsync_WithCorrectVersion_UpdatesSuccessfully()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);
            var saga = new TestSaga { CorrelationId = Guid.NewGuid(), CurrentState = "Initial" };
            await repository.SaveAsync(saga);

            // Act
            saga.CurrentState = "Updated";
            await repository.UpdateAsync(saga);

            // Assert
            var retrieved = await repository.FindAsync(saga.CorrelationId);
            Assert.NotNull(retrieved);
            Assert.Equal("Updated", retrieved.CurrentState);
        }

        [Fact]
        public async Task UpdateAsync_IncrementsVersion()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);
            var saga = new TestSaga { CorrelationId = Guid.NewGuid() };
            await repository.SaveAsync(saga);

            // Act
            await repository.UpdateAsync(saga);

            // Assert
            Assert.Equal(1, saga.Version);
        }

        [Fact]
        public async Task UpdateAsync_UpdatesTimestamp()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);
            var saga = new TestSaga { CorrelationId = Guid.NewGuid() };
            await repository.SaveAsync(saga);

            var originalUpdatedAt = saga.UpdatedAt;
            _timeProvider.Advance(TimeSpan.FromMinutes(5));

            // Act
            await repository.UpdateAsync(saga);

            // Assert
            Assert.NotEqual(originalUpdatedAt, saga.UpdatedAt);
            Assert.True(saga.UpdatedAt > originalUpdatedAt);
        }

        [Fact]
        public async Task UpdateAsync_WithIncorrectVersion_ThrowsSagaConcurrencyException()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);
            var saga = new TestSaga { CorrelationId = Guid.NewGuid() };
            await repository.SaveAsync(saga);

            // Simulate concurrent update
            await repository.UpdateAsync(saga);

            // Act - Try to update with stale version
            saga.Version = 0; // Reset to old version
            var ex = await Assert.ThrowsAsync<SagaConcurrencyException>(
                async () => await repository.UpdateAsync(saga));

            // Assert
            Assert.Equal(saga.CorrelationId, ex.CorrelationId);
            Assert.Equal(1, ex.ExpectedVersion);
            Assert.Equal(0, ex.ActualVersion);
        }

        [Fact]
        public async Task UpdateAsync_MultipleUpdates_IncrementsVersionCorrectly()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);
            var saga = new TestSaga { CorrelationId = Guid.NewGuid() };
            await repository.SaveAsync(saga);

            // Act
            await repository.UpdateAsync(saga);
            await repository.UpdateAsync(saga);
            await repository.UpdateAsync(saga);

            // Assert
            Assert.Equal(3, saga.Version);
        }

        [Fact]
        public async Task UpdateAsync_WithCancellation_ThrowsOperationCanceledException()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);
            var saga = new TestSaga { CorrelationId = Guid.NewGuid() };
            await repository.SaveAsync(saga);

            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await repository.UpdateAsync(saga, cts.Token));
        }

        #endregion

        #region DeleteAsync Tests

        [Fact]
        public async Task DeleteAsync_WithNonExistentCorrelationId_DoesNotThrow()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);

            // Act & Assert - should not throw
            await repository.DeleteAsync(Guid.NewGuid());
        }

        [Fact]
        public async Task DeleteAsync_WithExistingSaga_RemovesSaga()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);
            var saga = new TestSaga { CorrelationId = Guid.NewGuid() };
            await repository.SaveAsync(saga);

            // Act
            await repository.DeleteAsync(saga.CorrelationId);

            // Assert
            var result = await repository.FindAsync(saga.CorrelationId);
            Assert.Null(result);
            Assert.Equal(0, repository.Count);
        }

        [Fact]
        public async Task DeleteAsync_WithCancellation_ThrowsOperationCanceledException()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);
            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await repository.DeleteAsync(Guid.NewGuid(), cts.Token));
        }

        #endregion

        #region FindByStateAsync Tests

        [Fact]
        public async Task FindByStateAsync_WithNoSagas_ReturnsEmptyCollection()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);

            // Act
            var result = await repository.FindByStateAsync("TestState");

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public async Task FindByStateAsync_WithMatchingState_ReturnsSagas()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);
            var saga1 = new TestSaga { CorrelationId = Guid.NewGuid(), CurrentState = "Active" };
            var saga2 = new TestSaga { CorrelationId = Guid.NewGuid(), CurrentState = "Active" };
            var saga3 = new TestSaga { CorrelationId = Guid.NewGuid(), CurrentState = "Completed" };

            await repository.SaveAsync(saga1);
            await repository.SaveAsync(saga2);
            await repository.SaveAsync(saga3);

            // Act
            var result = await repository.FindByStateAsync("Active");

            // Assert
            var sagas = result.ToList();
            Assert.Equal(2, sagas.Count);
            Assert.All(sagas, s => Assert.Equal("Active", s.CurrentState));
        }

        [Fact]
        public async Task FindByStateAsync_WithNonMatchingState_ReturnsEmptyCollection()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);
            var saga = new TestSaga { CorrelationId = Guid.NewGuid(), CurrentState = "Active" };
            await repository.SaveAsync(saga);

            // Act
            var result = await repository.FindByStateAsync("Completed");

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public async Task FindByStateAsync_WithCancellation_ThrowsOperationCanceledException()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);
            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await repository.FindByStateAsync("TestState", cts.Token));
        }

        #endregion

        #region FindStaleAsync Tests

        [Fact]
        public async Task FindStaleAsync_WithNoStaleSagas_ReturnsEmptyCollection()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);
            var saga = new TestSaga { CorrelationId = Guid.NewGuid() };
            await repository.SaveAsync(saga);

            // Act
            var result = await repository.FindStaleAsync(TimeSpan.FromHours(1));

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public async Task FindStaleAsync_WithStaleSagas_ReturnsStaleSagas()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);
            var saga = new TestSaga { CorrelationId = Guid.NewGuid() };
            await repository.SaveAsync(saga);

            // Advance time to make saga stale
            _timeProvider.Advance(TimeSpan.FromHours(2));

            // Act
            var result = await repository.FindStaleAsync(TimeSpan.FromHours(1));

            // Assert
            var staleSagas = result.ToList();
            Assert.Single(staleSagas);
            Assert.Equal(saga.CorrelationId, staleSagas[0].CorrelationId);
        }

        [Fact]
        public async Task FindStaleAsync_WithCompletedSagas_DoesNotReturnCompletedSagas()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);
            var saga = new TestSaga { CorrelationId = Guid.NewGuid(), IsCompleted = true };
            await repository.SaveAsync(saga);

            // Advance time
            _timeProvider.Advance(TimeSpan.FromHours(2));

            // Act
            var result = await repository.FindStaleAsync(TimeSpan.FromHours(1));

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public async Task FindStaleAsync_WithRecentlyUpdatedSagas_DoesNotReturnThem()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);
            var saga = new TestSaga { CorrelationId = Guid.NewGuid() };
            await repository.SaveAsync(saga);

            // Advance time but not past threshold
            _timeProvider.Advance(TimeSpan.FromMinutes(30));

            // Act
            var result = await repository.FindStaleAsync(TimeSpan.FromHours(1));

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public async Task FindStaleAsync_WithMixedSagas_ReturnsOnlyStaleSagas()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);
            var oldSaga = new TestSaga { CorrelationId = Guid.NewGuid() };
            await repository.SaveAsync(oldSaga);

            _timeProvider.Advance(TimeSpan.FromHours(2));

            var newSaga = new TestSaga { CorrelationId = Guid.NewGuid() };
            await repository.SaveAsync(newSaga);

            // Act
            var result = await repository.FindStaleAsync(TimeSpan.FromHours(1));

            // Assert
            var staleSagas = result.ToList();
            Assert.Single(staleSagas);
            Assert.Equal(oldSaga.CorrelationId, staleSagas[0].CorrelationId);
        }

        [Fact]
        public async Task FindStaleAsync_WithCancellation_ThrowsOperationCanceledException()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);
            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await repository.FindStaleAsync(TimeSpan.FromHours(1), cts.Token));
        }

        #endregion

        #region GetAll and Clear Tests

        [Fact]
        public void GetAll_WithNoSagas_ReturnsEmptyCollection()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);

            // Act
            var result = repository.GetAll();

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public async Task GetAll_WithMultipleSagas_ReturnsAllSagas()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);
            var saga1 = new TestSaga { CorrelationId = Guid.NewGuid() };
            var saga2 = new TestSaga { CorrelationId = Guid.NewGuid() };
            var saga3 = new TestSaga { CorrelationId = Guid.NewGuid() };

            await repository.SaveAsync(saga1);
            await repository.SaveAsync(saga2);
            await repository.SaveAsync(saga3);

            // Act
            var result = repository.GetAll().ToList();

            // Assert
            Assert.Equal(3, result.Count);
        }

        [Fact]
        public void Clear_WithSagas_RemovesAllSagas()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);
            var saga1 = new TestSaga { CorrelationId = Guid.NewGuid() };
            var saga2 = new TestSaga { CorrelationId = Guid.NewGuid() };

            repository.SaveAsync(saga1).Wait();
            repository.SaveAsync(saga2).Wait();

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
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);

            // Act & Assert
            Assert.Equal(0, repository.Count);

            var saga1 = new TestSaga { CorrelationId = Guid.NewGuid() };
            repository.SaveAsync(saga1).Wait();
            Assert.Equal(1, repository.Count);

            var saga2 = new TestSaga { CorrelationId = Guid.NewGuid() };
            repository.SaveAsync(saga2).Wait();
            Assert.Equal(2, repository.Count);

            repository.DeleteAsync(saga1.CorrelationId).Wait();
            Assert.Equal(1, repository.Count);
        }

        #endregion

        #region Thread Safety Tests

        [Fact]
        public async Task ConcurrentSaves_WithDifferentCorrelationIds_AllSucceed()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);
            var tasks = new List<Task>();

            // Act
            for (int i = 0; i < 100; i++)
            {
                var saga = new TestSaga { CorrelationId = Guid.NewGuid() };
                tasks.Add(repository.SaveAsync(saga));
            }

            await Task.WhenAll(tasks);

            // Assert
            Assert.Equal(100, repository.Count);
        }

        [Fact]
        public async Task ConcurrentUpdates_WithSameCorrelationId_SomeSucceedSomeFail()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);
            var saga = new TestSaga { CorrelationId = Guid.NewGuid() };
            await repository.SaveAsync(saga);

            var tasks = new List<Task>();
            var successCount = 0;
            var failureCount = 0;

            // Act - Try concurrent updates
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var currentSaga = await repository.FindAsync(saga.CorrelationId);
                        if (currentSaga != null)
                        {
                            await repository.UpdateAsync(currentSaga);
                            Interlocked.Increment(ref successCount);
                        }
                    }
                    catch (SagaConcurrencyException)
                    {
                        Interlocked.Increment(ref failureCount);
                    }
                }));
            }

            await Task.WhenAll(tasks);

            // Assert - Some should succeed, some should fail due to concurrency
            Assert.True(successCount > 0);
            Assert.True(failureCount > 0);
            Assert.Equal(10, successCount + failureCount);
        }

        #endregion

        #region Test Helper Classes

        private class TestSaga : ISaga
        {
            public Guid CorrelationId { get; set; }
            public string CurrentState { get; set; } = "Initial";
            public DateTimeOffset CreatedAt { get; set; }
            public DateTimeOffset UpdatedAt { get; set; }
            public bool IsCompleted { get; set; }
            public int Version { get; set; }
        }

        #endregion
    }
}
