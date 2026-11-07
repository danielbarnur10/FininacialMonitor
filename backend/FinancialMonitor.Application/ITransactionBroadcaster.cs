using FinancialMonitor.Domain;

namespace FinancialMonitor.Application;

public interface ITransactionBroadcaster
{
    Task BroadcastAsync(
        Transaction transaction,
        CancellationToken cancellationToken = default
    );
}