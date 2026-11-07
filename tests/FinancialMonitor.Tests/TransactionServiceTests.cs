using FinancialMonitor.Application;
using FinancialMonitor.Domain;

namespace FinancialMonitor.Tests;

public class TransactionServiceTests
{
    [Fact]
    public async Task ProcessAsync_WaitsForSaveBeforeBroadcasting()
    {
        var saveCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new FakeStore { OnAdd = (_, _) => saveCompletion.Task };
        var broadcaster = new FakeBroadcaster();
        var service = new TransactionService(store, broadcaster);
        var transaction = NewTransaction();

        Task processing = service.ProcessAsync(transaction);
        bool broadcastBeforeSave = broadcaster.Calls > 0;
        saveCompletion.SetResult();
        await processing;

        Assert.False(broadcastBeforeSave);
        Assert.Same(transaction, store.Added);
        Assert.Same(transaction, broadcaster.Broadcast);
        Assert.Equal(1, broadcaster.Calls);
    }

    [Fact]
    public async Task ProcessAsync_WhenSaveFails_DoesNotBroadcast()
    {
        var store = new FakeStore { OnAdd = (_, _) => Task.FromException(new InvalidOperationException("save failed")) };
        var broadcaster = new FakeBroadcaster();
        var service = new TransactionService(store, broadcaster);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ProcessAsync(NewTransaction()));

        Assert.Equal(0, broadcaster.Calls);
    }

    [Fact]
    public async Task ProcessAsync_WhenBroadcastFails_ReportsErrorAfterSave()
    {
        var store = new FakeStore();
        var broadcaster = new FakeBroadcaster { OnBroadcast = (_, _) => Task.FromException(new InvalidOperationException("broadcast failed")) };
        var service = new TransactionService(store, broadcaster);
        var transaction = NewTransaction();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ProcessAsync(transaction));

        Assert.Same(transaction, store.Added);
        Assert.Same(transaction, broadcaster.Broadcast);
    }

    [Fact]
    public async Task ProcessAsync_ForwardsCancellationTokenToBothDependencies()
    {
        using var source = new CancellationTokenSource();
        var store = new FakeStore();
        var broadcaster = new FakeBroadcaster();
        var service = new TransactionService(store, broadcaster);

        await service.ProcessAsync(NewTransaction(), source.Token);

        Assert.Equal(source.Token, store.Token);
        Assert.Equal(source.Token, broadcaster.Token);
    }

    private static Transaction NewTransaction() => new(
        Guid.NewGuid(), 1500.50m, "USD", TransactionStatus.Pending, DateTimeOffset.UtcNow);

    private sealed class FakeStore : ITransactionStore
    {
        public Func<Transaction, CancellationToken, Task> OnAdd { get; init; } = (_, _) => Task.CompletedTask;
        public Transaction? Added { get; private set; }
        public CancellationToken Token { get; private set; }

        public Task AddAsync(Transaction transaction, CancellationToken cancellationToken = default)
        {
            Added = transaction;
            Token = cancellationToken;
            return OnAdd(transaction, cancellationToken);
        }

        public Task<IReadOnlyList<Transaction>> GetRecentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Transaction>>(Array.Empty<Transaction>());
    }

    private sealed class FakeBroadcaster : ITransactionBroadcaster
    {
        public Func<Transaction, CancellationToken, Task> OnBroadcast { get; init; } = (_, _) => Task.CompletedTask;
        public Transaction? Broadcast { get; private set; }
        public int Calls { get; private set; }
        public CancellationToken Token { get; private set; }

        public Task BroadcastAsync(Transaction transaction, CancellationToken cancellationToken = default)
        {
            Broadcast = transaction;
            Token = cancellationToken;
            Calls++;
            return OnBroadcast(transaction, cancellationToken);
        }
    }
}
