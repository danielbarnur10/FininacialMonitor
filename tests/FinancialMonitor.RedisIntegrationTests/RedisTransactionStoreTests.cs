using FinancialMonitor.Domain;
using FinancialMonitor.Infrastructure;
using StackExchange.Redis;

namespace FinancialMonitor.RedisIntegrationTests;

public class RedisTransactionStoreTests
{
    [Fact]
    public async Task TransactionSavedByOneStore_CanBeReadByAnotherStore()
    {
        // Arrange: two separate Redis connections.
        using var connectionA = await ConnectionMultiplexer.ConnectAsync("127.0.0.1:16379");

        using var connectionB = await ConnectionMultiplexer.ConnectAsync("127.0.0.1:16379");

        // Each test gets its own key, separate from real application data.
        var testKey = $"financial-monitor:test:{Guid.NewGuid():N}";

        try
        {
            var writer = new RedisTransactionStore(connectionA, testKey);
            var reader = new RedisTransactionStore(connectionB, testKey);

            var transaction = new Transaction(
                Guid.NewGuid(),
                1500.50m,
                "USD",
                TransactionStatus.Pending,
                DateTimeOffset.UtcNow
            );

            // Act
            await writer.AddAsync(transaction);
            var transactions = await reader.GetRecentAsync();

            // Assert
            var saved = Assert.Single(transactions);
            Assert.Equal(transaction, saved);
        }
        finally
        {
            // Delete only the key created by this test.
            await connectionA.GetDatabase().KeyDeleteAsync(testKey);
        }
    }

    [Fact]
    public async Task SameTransactionId_KeepsBothUpdates_NewestFirst()
    {
        using var connection = await ConnectionMultiplexer.ConnectAsync("127.0.0.1:16379");

        var testKey = $"financial-monitor:test:{Guid.NewGuid():N}";

        try
        {
            var store = new RedisTransactionStore(connection, testKey);
            var transactionId = Guid.NewGuid();

            var pending = CreateTransaction(transactionId, TransactionStatus.Pending);

            var completed = CreateTransaction(transactionId, TransactionStatus.Completed);

            await store.AddAsync(pending);
            await store.AddAsync(completed);

            var result = await store.GetRecentAsync();

            Assert.Equal(new[] { completed, pending }, result);
        }
        finally
        {
            await connection.GetDatabase().KeyDeleteAsync(testKey);
        }
    }

    [Fact]
public async Task ConcurrentWrites_KeepAtMostOneThousandTransactions()
{
    using var connectionA =
        await ConnectionMultiplexer.ConnectAsync("127.0.0.1:16379");

    using var connectionB =
        await ConnectionMultiplexer.ConnectAsync("127.0.0.1:16379");

    var testKey = $"financial-monitor:test:{Guid.NewGuid():N}";

    try
    {
        var storeA = new RedisTransactionStore(connectionA, testKey);
        var storeB = new RedisTransactionStore(connectionB, testKey);

        var transactions = Enumerable.Range(0, 1_100)
            .Select(_ => CreateTransaction())
            .ToArray();

        var sentIds = transactions
            .Select(transaction => transaction.TransactionId)
            .ToHashSet();

        // Start writes through both Redis connections.
        await Task.WhenAll(
            transactions.Select((transaction, index) =>
                index % 2 == 0
                    ? storeA.AddAsync(transaction)
                    : storeB.AddAsync(transaction)
            )
        );

        var result = await storeA.GetRecentAsync();

        // Check both the returned snapshot and the actual Redis list.
        var storedCount =
            await connectionA.GetDatabase().ListLengthAsync(testKey);

        Assert.Equal(1_000, result.Count);
        Assert.Equal(1_000L, storedCount);

        Assert.Equal(
            1_000,
            result.Select(transaction => transaction.TransactionId)
                .Distinct()
                .Count()
        );

        Assert.All(
            result,
            transaction => Assert.True(
                sentIds.Contains(transaction.TransactionId)
            )
        );
    }
    finally
    {
        await connectionA.GetDatabase().KeyDeleteAsync(testKey);
    }
}

private static Transaction CreateTransaction(
    Guid? transactionId = null,
    TransactionStatus status = TransactionStatus.Pending)
{
    return new Transaction(
        transactionId ?? Guid.NewGuid(),
        1500.50m,
        "USD",
        status,
        DateTimeOffset.UtcNow
    );
}
}
