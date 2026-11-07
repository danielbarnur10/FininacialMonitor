using FinancialMonitor.Application;
using FinancialMonitor.Domain;
using Microsoft.AspNetCore.Mvc;

namespace FinancialMonitor.Api.Controllers;

[ApiController]
[Route("api/transactions")]
public sealed class TransactionsController : ControllerBase
{
    private readonly ITransactionStore _store;
    private readonly TransactionService _service;
    private readonly ILogger<TransactionsController> _logger;

    public TransactionsController(
        ITransactionStore store,
        TransactionService service,
        ILogger<TransactionsController> logger)
    {
        _store = store;
        _service = service;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetRecent(CancellationToken cancellationToken)
    {
        var transactions = await _store.GetRecentAsync(cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return Ok(transactions.Select(TransactionResponse.FromDomain));
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] TransactionRequest request,
        CancellationToken cancellationToken)
    {
        var errors = request.ValidateRequiredFields();
        if (errors.Count > 0)
            return Invalid(errors);

        if (!TransactionRequest.TryParseStatus(request.Status!, out var status))
            return Invalid(new Dictionary<string, string[]>
            {
                ["status"] = ["Status must be Pending, Completed, or Failed."]
            });

        Transaction transaction;
        try
        {
            transaction = new Transaction(
                request.TransactionId!.Value,
                request.Amount!.Value,
                request.Currency!,
                status,
                request.Timestamp!.Value);
        }
        catch (ArgumentException exception)
        {
            return Invalid(new Dictionary<string, string[]>
            {
                [exception.ParamName ?? "transaction"] = [exception.Message]
            });
        }

        try
        {
            await _service.ProcessAsync(transaction, cancellationToken);
        }
        catch (TransactionBroadcastException exception)
        {
            // A delivery failure must not imply the transaction was not stored.
            _logger.LogError(exception, "Transaction {TransactionId} was stored but broadcast failed", transaction.TransactionId);
            return StatusCode(StatusCodes.Status202Accepted, new
            {
                transactionId = transaction.TransactionId,
                stored = true,
                broadcastSucceeded = false,
                message = "Transaction was stored, but the live broadcast failed."
            });
        }

        return StatusCode(StatusCodes.Status201Created, TransactionResponse.FromDomain(transaction));
    }

    private ActionResult Invalid(Dictionary<string, string[]> errors) =>
        ValidationProblem(new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest
        });
}
