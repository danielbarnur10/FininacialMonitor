using FinancialMonitor.Application;
using FinancialMonitor.Domain;
using Microsoft.AspNetCore.SignalR;

namespace FinancialMonitor.Api;

public sealed class SignalRTransactionBroadcaster(IHubContext<TransactionHub> hub) : ITransactionBroadcaster
{
    public async Task BroadcastAsync(Transaction transaction, CancellationToken cancellationToken = default)
    {
        try
        {
            await hub.Clients.All.SendAsync(
                "TransactionReceived", TransactionResponse.FromDomain(transaction), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new TransactionBroadcastException(exception);
        }
    }
}

public sealed class TransactionBroadcastException(Exception innerException)
    : Exception("The live transaction broadcast failed.", innerException)
{
}
