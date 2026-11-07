using FinancialMonitor.Domain;

namespace FinancialMonitor.Api;

public sealed record TransactionResponse(
    Guid TransactionId,
    decimal Amount,
    string Currency,
    string Status,
    DateTimeOffset Timestamp)
{
    public static TransactionResponse FromDomain(Transaction transaction) => new(
        transaction.TransactionId,
        transaction.Amount,
        transaction.Currency,
        transaction.Status.ToString(),
        transaction.Timestamp);
}
