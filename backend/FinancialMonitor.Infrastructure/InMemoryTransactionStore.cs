using FinancialMonitor.Application;
using FinancialMonitor.Domain;

namespace FinancialMonitor.Infrastructure;

/// <summary>
/// Keeps a bounded history in one process. All operations use the same lock,
/// so a reader sees a consistent point-in-time copy while writers are active.
/// </summary>
public sealed class InMemoryTransactionStore : ITransactionStore
{
    public const int MaxRetainedTransactions = 1_000;

    private readonly object _gate = new();
    private readonly Queue<Transaction> _transactions = new();

    public Task AddAsync(Transaction transaction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _transactions.Enqueue(transaction);

            if (_transactions.Count > MaxRetainedTransactions)
            {
                _transactions.Dequeue();
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Transaction>> GetRecentAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Queue enumerates oldest first; return a separate newest-first snapshot.
            IReadOnlyList<Transaction> snapshot = _transactions.Reverse().ToArray();
            return Task.FromResult(snapshot);
        }
    }
}
