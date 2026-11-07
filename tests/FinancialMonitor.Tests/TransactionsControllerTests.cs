using System.Text.Json;
using FinancialMonitor.Api;
using FinancialMonitor.Api.Controllers;
using FinancialMonitor.Application;
using FinancialMonitor.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinancialMonitor.Tests;

public class TransactionsControllerTests
{
    [Fact]
    public async Task Create_WhenBroadcastFailsAfterSave_ReportsThatTransactionWasStored()
    {
        var store = new RecordingStore();
        var service = new TransactionService(store, new FailingBroadcaster());
        var controller = new TransactionsController(store, service, NullLogger<TransactionsController>.Instance);
        var id = Guid.NewGuid();
        var request = new TransactionRequest(id, 100m, "USD", "Pending", DateTimeOffset.UtcNow);

        var result = Assert.IsType<ObjectResult>(await controller.Create(request, CancellationToken.None));

        Assert.Equal(202, result.StatusCode);
        Assert.Equal(id, store.Saved?.TransactionId);
        var body = JsonSerializer.SerializeToElement(result.Value);
        Assert.True(body.GetProperty("stored").GetBoolean());
        Assert.False(body.GetProperty("broadcastSucceeded").GetBoolean());
    }

    private sealed class RecordingStore : ITransactionStore
    {
        public Transaction? Saved { get; private set; }

        public Task AddAsync(Transaction transaction, CancellationToken cancellationToken = default)
        {
            Saved = transaction;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Transaction>> GetRecentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Transaction>>(Array.Empty<Transaction>());
    }

    private sealed class FailingBroadcaster : ITransactionBroadcaster
    {
        public Task BroadcastAsync(Transaction transaction, CancellationToken cancellationToken = default) =>
            Task.FromException(new TransactionBroadcastException(new InvalidOperationException("SignalR failure")));
    }
}
