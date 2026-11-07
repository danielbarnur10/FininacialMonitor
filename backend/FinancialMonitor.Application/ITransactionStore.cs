using FinancialMonitor.Domain;

namespace FinancialMonitor.Application;

public interface ITransactionStore
{
    Task AddAsync(
        Transaction transaction,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyList<Transaction>> GetRecentAsync(
        CancellationToken cancellationToken = default
    );
}
