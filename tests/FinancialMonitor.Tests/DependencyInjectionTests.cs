using FinancialMonitor.Api.IoC;
using FinancialMonitor.Application;
using FinancialMonitor.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.StackExchangeRedis;
namespace FinancialMonitor.Tests;

public class DependencyInjectionTests
{
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
