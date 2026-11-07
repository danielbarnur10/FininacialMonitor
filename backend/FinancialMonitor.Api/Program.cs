using FinancialMonitor.Api;
using FinancialMonitor.Api.IoC;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFinancialMonitor(builder.Configuration);

var app = builder.Build();

app.UseCors();
app.MapControllers();
app.MapHub<TransactionHub>("/hubs/transactions");

app.Run();
