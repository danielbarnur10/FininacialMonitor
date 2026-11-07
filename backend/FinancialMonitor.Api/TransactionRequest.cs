using FinancialMonitor.Domain;

namespace FinancialMonitor.Api;

// Nullable properties distinguish omitted JSON fields from default values.
public sealed record TransactionRequest(
    Guid? TransactionId,
    decimal? Amount,
    string? Currency,
    string? Status,
    DateTimeOffset? Timestamp)
{
    public Dictionary<string, string[]> ValidateRequiredFields()
    {
        var errors = new Dictionary<string, string[]>();
        if (TransactionId is null) errors["transactionId"] = ["Transaction ID is required."];
        if (Amount is null) errors["amount"] = ["Amount is required."];
        if (Currency is null) errors["currency"] = ["Currency is required."];
        if (Status is null) errors["status"] = ["Status is required."];
        if (Timestamp is null) errors["timestamp"] = ["Timestamp is required."];
        return errors;
    }

    public static bool TryParseStatus(string value, out TransactionStatus status)
    {
        switch (value)
        {
            case "Pending": status = TransactionStatus.Pending; return true;
            case "Completed": status = TransactionStatus.Completed; return true;
            case "Failed": status = TransactionStatus.Failed; return true;
            default: status = default; return false;
        }
    }
}
