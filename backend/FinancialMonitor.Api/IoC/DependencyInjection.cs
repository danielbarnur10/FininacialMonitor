using FinancialMonitor.Application;
using FinancialMonitor.Infrastructure;
using StackExchange.Redis;

namespace FinancialMonitor.Api.IoC;

/// <summary>Registers the backend's dependencies at the application boundary.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddFinancialMonitor(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddControllers();
        var signalR = services.AddSignalR();

        var redisConnection = configuration.GetConnectionString("Redis");

        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            signalR.AddStackExchangeRedis(redisConnection);
            services.AddSingleton<IConnectionMultiplexer>(
                _ => ConnectionMultiplexer.Connect(redisConnection));
            services.AddSingleton<ITransactionStore>(provider =>
                new RedisTransactionStore(
                    provider.GetRequiredService<IConnectionMultiplexer>(),
                    "financial-monitor:transactions"));
        }
        else
        {
            // Share one local history when Redis is not configured.
            services.AddSingleton<ITransactionStore, InMemoryTransactionStore>();
        }

        services.AddSingleton<ITransactionBroadcaster, SignalRTransactionBroadcaster>();

        // A service instance is created for each HTTP request.
        services.AddScoped<TransactionService>();

        // Additional origins must be configured deliberately for other environments.
        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(options =>
            options.AddDefaultPolicy(policy =>
                policy
                    .WithOrigins(allowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials()
            )
        );

        return services;
    }
}
