using System.Text.Json;
using FinancialMonitor.Application;
using FinancialMonitor.Domain;
using StackExchange.Redis;

namespace FinancialMonitor.Infrastructure;

public sealed class RedisTransactionStore : ITransactionStore
{
    private const int MaxRetainedTransactions = 1_000;

    private const string AddAndTrimScript = """
        redis.call('LPUSH', KEYS[1], ARGV[1])
        redis.call('LTRIM', KEYS[1], 0, ARGV[2])
        return 1
        """;

    private readonly IDatabase _database;
    private readonly RedisKey _key;

    public RedisTransactionStore(
        IConnectionMultiplexer connection,
        string key)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        _database = connection.GetDatabase();
        _key = key;
    }

    public async Task AddAsync(
        Transaction transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        var json = JsonSerializer.Serialize(transaction);

        cancellationToken.ThrowIfCancellationRequested();

        await _database.ScriptEvaluateAsync(
            AddAndTrimScript,
            new RedisKey[] { _key },
            new RedisValue[] { json, MaxRetainedTransactions - 1 }
        );
    }

    public async Task<IReadOnlyList<Transaction>> GetRecentAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var values = await _database.ListRangeAsync(
            _key,
            0,
            MaxRetainedTransactions - 1
        );

        var transactions = new List<Transaction>(values.Length);

        foreach (var value in values)
        {
            var transaction =
                JsonSerializer.Deserialize<Transaction>(value.ToString())
                ?? throw new InvalidDataException(
                    "A stored transaction was null."
                );

            transactions.Add(transaction);
        }

        return transactions;
    }
}