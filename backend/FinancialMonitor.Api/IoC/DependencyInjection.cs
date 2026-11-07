using FinancialMonitor.Application;
using FinancialMonitor.Infrastructure;

namespace FinancialMonitor.Api.IoC;

/// <summary>Registers the backend's dependencies at the application boundary.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddFinancialMonitor(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddControllers();
        services.AddSignalR();

        // All requests and hub connections must see the same in-memory history.
        services.AddSingleton<ITransactionStore, InMemoryTransactionStore>();
        services.AddSingleton<ITransactionBroadcaster, SignalRTransactionBroadcaster>();

        // A service instance is created for each HTTP request.
        services.AddScoped<TransactionService>();

        // Additional origins must be configured deliberately for other environments.
        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(options => options.AddDefaultPolicy(policy =>
            policy.WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials()));

        return services;
    }
}
