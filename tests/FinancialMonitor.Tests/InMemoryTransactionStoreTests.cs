using FinancialMonitor.Domain;
using FinancialMonitor.Infrastructure;

namespace FinancialMonitor.Tests;

public class InMemoryTransactionStoreTests
{
    [Fact]
    public async Task GetRecent_ReturnsNewestFirst_AndPreviousSnapshotDoesNotChange()
    {
        var store = new InMemoryTransactionStore();
        var first = NewTransaction();
        var second = NewTransaction();
        await store.AddAsync(first);
        var snapshot = await store.GetRecentAsync();

        await store.AddAsync(second);

        Assert.Equal(new[] { first }, snapshot);
        Assert.Equal(new[] { second, first }, await store.GetRecentAsync());
    }

    [Fact]
    public async Task GetRecent_RetainsOnlyMostRecentThousand()
    {
        var store = new InMemoryTransactionStore();
        var transactions = Enumerable.Range(0, 1010).Select(_ => NewTransaction()).ToArray();
        foreach (var transaction in transactions)
        {
            await store.AddAsync(transaction);
        }

        var snapshot = await store.GetRecentAsync();

        Assert.Equal(1000, snapshot.Count);
        Assert.Equal(transactions.Skip(10).Reverse(), snapshot);
    }

    [Fact]
    public async Task Add_ReportsRepeatedIdsAsSeparateUpdates()
    {
        var store = new InMemoryTransactionStore();
        var id = Guid.NewGuid();
        var pending = NewTransaction(id, TransactionStatus.Pending);
        var completed = NewTransaction(id, TransactionStatus.Completed);

        await store.AddAsync(pending);
        await store.AddAsync(completed);

        Assert.Equal(new[] { completed, pending }, await store.GetRecentAsync());
    }

    [Fact]
    public async Task ConcurrentReadsAndWrites_DoNotLoseOrDuplicateAcceptedTransactions()
    {
        var store = new InMemoryTransactionStore();
        var transactions = Enumerable.Range(0, 500).Select(_ => NewTransaction()).ToArray();
        var expectedIds = transactions.Select(t => t.TransactionId).ToHashSet();

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task[] writers = transactions.Select(transaction => Task.Run(async () =>
        {
            await start.Task;
            await store.AddAsync(transaction);
        })).ToArray();
        Task[] readers = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            await start.Task;
            for (int i = 0; i < 50; i++)
            {
                var snapshot = await store.GetRecentAsync();
                var ids = snapshot.Select(t => t.TransactionId).ToArray();
                Assert.Equal(ids.Length, ids.Distinct().Count());
                Assert.All(ids, id => Assert.Contains(id, expectedIds));
            }
        })).ToArray();

        start.SetResult();
        await Task.WhenAll(writers.Concat(readers));

        var final = await store.GetRecentAsync();
        Assert.Equal(transactions.Length, final.Count);
        Assert.True(expectedIds.SetEquals(final.Select(t => t.TransactionId)));
    }

    [Fact]
    public async Task CancelledOperations_DoNotAddTransactions()
    {
        var store = new InMemoryTransactionStore();
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.AddAsync(NewTransaction(), source.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.GetRecentAsync(source.Token));

        Assert.Empty(await store.GetRecentAsync());
    }

    private static Transaction NewTransaction(Guid? id = null, TransactionStatus status = TransactionStatus.Pending) =>
        new(id ?? Guid.NewGuid(), 100m, "USD", status, DateTimeOffset.UtcNow);
}
