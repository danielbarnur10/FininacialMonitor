namespace FinancialMonitor.Domain;

public record Transaction
{
    public Guid TransactionId { get; }
    public decimal Amount { get; }
    public string Currency { get; }
    public TransactionStatus Status { get; }
    public DateTimeOffset Timestamp { get; }

    public Transaction(
        Guid transactionId,
        decimal amount,
        string currency,
        TransactionStatus status,
        DateTimeOffset timestamp
    )
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                amount,
                "Amount must be greater than zero."
            );
        }
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "Status must be Pending, Completed, or Failed."
            );
        }
        if (transactionId == Guid.Empty)
        {
            throw new ArgumentException("Transaction ID must not be empty.", nameof(transactionId));
        }
        if (
            currency is null
            || currency.Length != 3
            || currency.Any(character => character is < 'A' or > 'Z')
        )
        {
            throw new ArgumentException(
                "Currency must contain three uppercase letters.",
                nameof(currency)
            );
        }
        if (timestamp == default)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timestamp),
                timestamp,
                "Timestamp must be provided."
            );
        }
        TransactionId = transactionId;
        Amount = amount;
        Currency = currency;
        Status = status;
        Timestamp = timestamp;
    }
}
