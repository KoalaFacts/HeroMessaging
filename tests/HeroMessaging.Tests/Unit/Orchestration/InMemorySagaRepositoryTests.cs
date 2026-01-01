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
                async () => await repository.SaveAsync(null!, TestContext.Current.CancellationToken));
            Assert.Equal("saga", ex.ParamName);
        }

        [Fact]
        public async Task SaveAsync_WithNewSaga_SavesSuccessfully()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);
            var saga = new TestSaga { CorrelationId = Guid.NewGuid() };

            // Act
            await repository.SaveAsync(saga, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(1, repository.Count);
            var retrieved = await repository.FindAsync(saga.CorrelationId, TestContext.Current.CancellationToken);
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
            await repository.SaveAsync(saga, TestContext.Current.CancellationToken);

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
            await repository.SaveAsync(saga, TestContext.Current.CancellationToken);

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
            await repository.SaveAsync(saga, TestContext.Current.CancellationToken);

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

            await repository.SaveAsync(saga1, TestContext.Current.CancellationToken);

            // Act & Assert
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await repository.SaveAsync(saga2, TestContext.Current.CancellationToken));
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
                async () => await repository.SaveAsync(saga, cts.Token, TestContext.Current.CancellationToken));
        }

        #endregion

        #region FindAsync Tests

        [Fact]
        public async Task FindAsync_WithNonExistentCorrelationId_ReturnsNull()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);

            // Act
            var result = await repository.FindAsync(Guid.NewGuid(, TestContext.Current.CancellationToken));

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task FindAsync_WithExistingCorrelationId_ReturnsSaga()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);
            var saga = new TestSaga { CorrelationId = Guid.NewGuid(), CurrentState = "TestState" };
            await repository.SaveAsync(saga, TestContext.Current.CancellationToken);

            // Act
            var result = await repository.FindAsync(saga.CorrelationId, TestContext.Current.CancellationToken);

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
                async () => await repository.UpdateAsync(null!, TestContext.Current.CancellationToken));
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
                async () => await repository.UpdateAsync(saga, TestContext.Current.CancellationToken));
            Assert.Contains("not found", ex.Message);
            Assert.Contains("SaveAsync", ex.Message);
        }

        [Fact]
        public async Task UpdateAsync_WithCorrectVersion_UpdatesSuccessfully()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);
            var saga = new TestSaga { CorrelationId = Guid.NewGuid(), CurrentState = "Initial" };
            await repository.SaveAsync(saga, TestContext.Current.CancellationToken);

            // Act
            saga.CurrentState = "Updated";
            await repository.UpdateAsync(saga, TestContext.Current.CancellationToken);

            // Assert
            var retrieved = await repository.FindAsync(saga.CorrelationId, TestContext.Current.CancellationToken);
            Assert.NotNull(retrieved);
            Assert.Equal("Updated", retrieved.CurrentState);
        }

        [Fact]
        public async Task UpdateAsync_IncrementsVersion()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);
            var saga = new TestSaga { CorrelationId = Guid.NewGuid() };
            await repository.SaveAsync(saga, TestContext.Current.CancellationToken);

            // Act
            await repository.UpdateAsync(saga, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(1, saga.Version);
        }

        [Fact]
        public async Task UpdateAsync_UpdatesTimestamp()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);
            var saga = new TestSaga { CorrelationId = Guid.NewGuid() };
            await repository.SaveAsync(saga, TestContext.Current.CancellationToken);

            var originalUpdatedAt = saga.UpdatedAt;
            _timeProvider.Advance(TimeSpan.FromMinutes(5));

            // Act
            await repository.UpdateAsync(saga, TestContext.Current.CancellationToken);

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
            await repository.SaveAsync(saga, TestContext.Current.CancellationToken);

            // Simulate concurrent update
            await repository.UpdateAsync(saga, TestContext.Current.CancellationToken);

            // Act - Try to update with stale version
            saga.Version = 0; // Reset to old version
            var ex = await Assert.ThrowsAsync<SagaConcurrencyException>(
                async () => await repository.UpdateAsync(saga, TestContext.Current.CancellationToken));

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
            await repository.SaveAsync(saga, TestContext.Current.CancellationToken);

            // Act
            await repository.UpdateAsync(saga, TestContext.Current.CancellationToken);
            await repository.UpdateAsync(saga, TestContext.Current.CancellationToken);
            await repository.UpdateAsync(saga, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(3, saga.Version);
        }

        [Fact]
        public async Task UpdateAsync_WithCancellation_ThrowsOperationCanceledException()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);
            var saga = new TestSaga { CorrelationId = Guid.NewGuid() };
            await repository.SaveAsync(saga, TestContext.Current.CancellationToken);

            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await repository.UpdateAsync(saga, cts.Token, TestContext.Current.CancellationToken));
        }

        #endregion

        #region DeleteAsync Tests

        [Fact]
        public async Task DeleteAsync_WithNonExistentCorrelationId_DoesNotThrow()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);

            // Act & Assert - should not throw
            await repository.DeleteAsync(Guid.NewGuid(, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task DeleteAsync_WithExistingSaga_RemovesSaga()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);
            var saga = new TestSaga { CorrelationId = Guid.NewGuid() };
            await repository.SaveAsync(saga, TestContext.Current.CancellationToken);

            // Act
            await repository.DeleteAsync(saga.CorrelationId, TestContext.Current.CancellationToken);

            // Assert
            var result = await repository.FindAsync(saga.CorrelationId, TestContext.Current.CancellationToken);
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
            var result = await repository.FindByStateAsync("TestState", TestContext.Current.CancellationToken);

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

            await repository.SaveAsync(saga1, TestContext.Current.CancellationToken);
            await repository.SaveAsync(saga2, TestContext.Current.CancellationToken);
            await repository.SaveAsync(saga3, TestContext.Current.CancellationToken);

            // Act
            var result = await repository.FindByStateAsync("Active", TestContext.Current.CancellationToken);

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
            await repository.SaveAsync(saga, TestContext.Current.CancellationToken);

            // Act
            var result = await repository.FindByStateAsync("Completed", TestContext.Current.CancellationToken);

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
                async () => await repository.FindByStateAsync("TestState", cts.Token, TestContext.Current.CancellationToken));
        }

        #endregion

        #region FindStaleAsync Tests

        [Fact]
        public async Task FindStaleAsync_WithNoStaleSagas_ReturnsEmptyCollection()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);
            var saga = new TestSaga { CorrelationId = Guid.NewGuid() };
            await repository.SaveAsync(saga, TestContext.Current.CancellationToken);

            // Act
            var result = await repository.FindStaleAsync(TimeSpan.FromHours(1, TestContext.Current.CancellationToken));

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public async Task FindStaleAsync_WithStaleSagas_ReturnsStaleSagas()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);
            var saga = new TestSaga { CorrelationId = Guid.NewGuid() };
            await repository.SaveAsync(saga, TestContext.Current.CancellationToken);

            // Advance time to make saga stale
            _timeProvider.Advance(TimeSpan.FromHours(2));

            // Act
            var result = await repository.FindStaleAsync(TimeSpan.FromHours(1, TestContext.Current.CancellationToken));

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
            await repository.SaveAsync(saga, TestContext.Current.CancellationToken);

            // Advance time
            _timeProvider.Advance(TimeSpan.FromHours(2));

            // Act
            var result = await repository.FindStaleAsync(TimeSpan.FromHours(1, TestContext.Current.CancellationToken));

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public async Task FindStaleAsync_WithRecentlyUpdatedSagas_DoesNotReturnThem()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);
            var saga = new TestSaga { CorrelationId = Guid.NewGuid() };
            await repository.SaveAsync(saga, TestContext.Current.CancellationToken);

            // Advance time but not past threshold
            _timeProvider.Advance(TimeSpan.FromMinutes(30));

            // Act
            var result = await repository.FindStaleAsync(TimeSpan.FromHours(1, TestContext.Current.CancellationToken));

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public async Task FindStaleAsync_WithMixedSagas_ReturnsOnlyStaleSagas()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);
            var oldSaga = new TestSaga { CorrelationId = Guid.NewGuid() };
            await repository.SaveAsync(oldSaga, TestContext.Current.CancellationToken);

            _timeProvider.Advance(TimeSpan.FromHours(2));

            var newSaga = new TestSaga { CorrelationId = Guid.NewGuid() };
            await repository.SaveAsync(newSaga, TestContext.Current.CancellationToken);

            // Act
            var result = await repository.FindStaleAsync(TimeSpan.FromHours(1, TestContext.Current.CancellationToken));

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

            await repository.SaveAsync(saga1, TestContext.Current.CancellationToken);
            await repository.SaveAsync(saga2, TestContext.Current.CancellationToken);
            await repository.SaveAsync(saga3, TestContext.Current.CancellationToken);

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
        public async Task ConcurrentUpdates_WithSameCorrelationId_AllSucceedWithLocking()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);
            var saga = new TestSaga { CorrelationId = Guid.NewGuid() };
            await repository.SaveAsync(saga, TestContext.Current.CancellationToken);

            var tasks = new List<Task>();
            var successCount = 0;
            var failureCount = 0;

            // Act - Try concurrent updates
            // Note: With in-memory storage using shared references, concurrent updates
            // are serialized by the internal lock and all succeed because they share
            // the same saga reference (version always matches).
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var currentSaga = await repository.FindAsync(saga.CorrelationId, TestContext.Current.CancellationToken);
                        if (currentSaga != null)
                        {
                            await repository.UpdateAsync(currentSaga, TestContext.Current.CancellationToken);
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

            // Assert - All updates should succeed with in-memory shared reference model
            // Each concurrent update is serialized and version stays in sync
            Assert.Equal(10, successCount);
            Assert.Equal(0, failureCount);
            Assert.Equal(10, successCount + failureCount);

            // Version should be incremented 10 times (once per update)
            var finalSaga = await repository.FindAsync(saga.CorrelationId, TestContext.Current.CancellationToken);
            Assert.NotNull(finalSaga);
            Assert.Equal(10, finalSaga!.Version);
        }

        [Fact]
        public async Task ConcurrentUpdates_WithStaleVersion_ThrowsSagaConcurrencyException()
        {
            // Arrange
            var repository = new InMemorySagaRepository<TestSaga>(_timeProvider);
            var saga = new TestSaga { CorrelationId = Guid.NewGuid() };
            await repository.SaveAsync(saga, TestContext.Current.CancellationToken);

            // Get initial reference and capture version
            var staleSaga = await repository.FindAsync(saga.CorrelationId, TestContext.Current.CancellationToken);
            Assert.NotNull(staleSaga);

            // Update the saga to increment version
            await repository.UpdateAsync(staleSaga!, TestContext.Current.CancellationToken);

            // Create a new saga instance with the OLD version to simulate stale read
            var staleSagaCopy = new TestSaga
            {
                CorrelationId = saga.CorrelationId,
                CurrentState = staleSaga.CurrentState,
                Version = 0 // Old version before the update
            };

            // Act & Assert - Trying to update with stale version should fail
            await Assert.ThrowsAsync<SagaConcurrencyException>(
                () => repository.UpdateAsync(staleSagaCopy, TestContext.Current.CancellationToken));
        }

        #endregion

        #region Test Helper Classes

        public class TestSaga : ISaga
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
