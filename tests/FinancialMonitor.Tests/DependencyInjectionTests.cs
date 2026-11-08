using FinancialMonitor.Api.IoC;
using FinancialMonitor.Application;
using FinancialMonitor.Domain;
using FinancialMonitor.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.StackExchangeRedis;
using StackExchange.Redis;
namespace FinancialMonitor.Tests;

public class DependencyInjectionTests
{
    [Fact]
    public void RedisConfigured_RegistersSingletonSharedStoreAndConnection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Redis"] = "127.0.0.1:16379"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddFinancialMonitor(configuration);

        var store = Assert.Single(services, item => item.ServiceType == typeof(ITransactionStore));
        var connection = Assert.Single(services, item => item.ServiceType == typeof(IConnectionMultiplexer));
        Assert.Equal(ServiceLifetime.Singleton, store.Lifetime);
        Assert.NotNull(store.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Singleton, connection.Lifetime);
        Assert.NotNull(connection.ImplementationFactory);
    }

    [Fact]
    public void RedisNotConfigured_UsesLocalInMemoryStore()
    {
        var services = new ServiceCollection();

        services.AddFinancialMonitor(new ConfigurationBuilder().Build());

        var store = Assert.Single(services, item => item.ServiceType == typeof(ITransactionStore));
        Assert.Equal(typeof(InMemoryTransactionStore), store.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, store.Lifetime);
        Assert.DoesNotContain(services, item => item.ServiceType == typeof(IConnectionMultiplexer));
    }

    [Fact]
    public async Task Store_IsSharedBetweenRequestScopes()
    {
        using var provider = CreateProvider();
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();
        var firstStore = firstScope.ServiceProvider.GetRequiredService<ITransactionStore>();
        var secondStore = secondScope.ServiceProvider.GetRequiredService<ITransactionStore>();
        var transaction = new Transaction(
            Guid.NewGuid(), 100m, "USD", TransactionStatus.Pending, DateTimeOffset.UtcNow);

        await firstStore.AddAsync(transaction);

        Assert.Same(firstStore, secondStore);
        Assert.Contains(transaction, await secondStore.GetRecentAsync());
    }

    [Fact]
    public void TransactionService_IsScoped_AndBroadcasterIsShared()
    {
        using var provider = CreateProvider();
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        var first = firstScope.ServiceProvider.GetRequiredService<TransactionService>();
        var sameScope = firstScope.ServiceProvider.GetRequiredService<TransactionService>();
        var second = secondScope.ServiceProvider.GetRequiredService<TransactionService>();
        var firstBroadcaster = firstScope.ServiceProvider.GetRequiredService<ITransactionBroadcaster>();
        var secondBroadcaster = secondScope.ServiceProvider.GetRequiredService<ITransactionBroadcaster>();

        Assert.Same(first, sameScope);
        Assert.NotSame(first, second);
        Assert.Same(firstBroadcaster, secondBroadcaster);
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFinancialMonitor(new ConfigurationBuilder().Build());
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
    [Fact]
public void RedisConfigured_RegistersRedisSignalRBackplane()
{
    // Arrange
    var settings = new Dictionary<string, string?>
    {
        ["ConnectionStrings:Redis"] = "127.0.0.1:16379"
    };

    var configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(settings)
        .Build();

    var services = new ServiceCollection();

    // Act
    services.AddFinancialMonitor(configuration);

    // Assert
    var registration = services.Last(
        service => service.ServiceType == typeof(HubLifetimeManager<>)
    );

    Assert.Equal(
        typeof(RedisHubLifetimeManager<>),
        registration.ImplementationType
    );
}
}
