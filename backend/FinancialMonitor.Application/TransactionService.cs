using FinancialMonitor.Domain;

namespace FinancialMonitor.Application;

public class TransactionService
{
    private readonly ITransactionStore _store;
    private readonly ITransactionBroadcaster _broadcaster;

    public TransactionService(
        ITransactionStore store,
        ITransactionBroadcaster broadcaster)
    {
        _store = store;
        _broadcaster = broadcaster;
    }

    public async Task ProcessAsync(
        Transaction transaction,
        CancellationToken cancellationToken = default)
    {
        await _store.AddAsync(transaction, cancellationToken);

        await _broadcaster.BroadcastAsync(transaction, cancellationToken);
    }
}
