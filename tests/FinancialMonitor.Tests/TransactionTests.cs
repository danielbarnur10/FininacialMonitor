using FinancialMonitor.Domain;

namespace FinancialMonitor.Tests;

public class TransactionTests
{
    [Fact]
    public void Create_WithZeroAmount_ThrowsAnException()
    {
        // Arrange
        var transactionId = Guid.NewGuid();
        var timestamp = DateTimeOffset.UtcNow;

        // Act and Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Transaction(
                transactionId,
                0m,
                "USD",
                TransactionStatus.Pending,
                timestamp
            )
        );
    }

    [Fact]
    public void Create_WithPositiveAmount_PreservesTheAmount()
    {
        // Arrange and Act
        var transaction = new Transaction(
            Guid.NewGuid(),
            1500.50m,
            "USD",
            TransactionStatus.Pending,
            DateTimeOffset.UtcNow
        );

        // Assert
        Assert.Equal(1500.50m, transaction.Amount);
    }

    [Fact]
    public void Create_WithNegativeAmount_ThrowsAnException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Transaction(
                Guid.NewGuid(),
                -10m,
                "USD",
                TransactionStatus.Pending,
                DateTimeOffset.UtcNow
            )
        );
    }

    [Fact]
    public void Create_WithUnknownStatus_ThrowsAnException()
    {
        var unknownStatus = (TransactionStatus)999;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Transaction(
                Guid.NewGuid(),
                100m,
                "USD",
                unknownStatus,
                DateTimeOffset.UtcNow
            )
        );
    }

    [Theory]
    [InlineData(TransactionStatus.Pending)]
    [InlineData(TransactionStatus.Completed)]
    [InlineData(TransactionStatus.Failed)]
    public void Create_WithValidStatus_PreservesTheStatus(TransactionStatus status)
    {
        var transaction = new Transaction(
            Guid.NewGuid(),
            100m,
            "USD",
            status,
            DateTimeOffset.UtcNow
        );

        Assert.Equal(status, transaction.Status);
    }

    [Fact]
    public void Create_WithEmptyTransactionId_ThrowsAnException()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new Transaction(
                Guid.Empty,
                100m,
                "USD",
                TransactionStatus.Pending,
                DateTimeOffset.UtcNow
            )
        );

        Assert.Equal("transactionId", exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("US")]
    [InlineData("USDD")]
    [InlineData("usd")]
    [InlineData("US1")]
    public void Create_WithInvalidCurrency_ThrowsAnException(string? currency)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new Transaction(
                Guid.NewGuid(),
                100m,
                currency!,
                TransactionStatus.Pending,
                DateTimeOffset.UtcNow
            )
        );

        Assert.Equal("currency", exception.ParamName);
    }

    [Theory]
    [InlineData("USD")]
    [InlineData("ILS")]
    [InlineData("EUR")]
    public void Create_WithValidCurrency_PreservesTheCurrency(string currency)
    {
        var transaction = new Transaction(
            Guid.NewGuid(),
            100m,
            currency,
            TransactionStatus.Pending,
            DateTimeOffset.UtcNow
        );

        Assert.Equal(currency, transaction.Currency);
    }

    [Fact]
    public void Create_WithDefaultTimestamp_ThrowsAnException()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Transaction(
                Guid.NewGuid(),
                100m,
                "USD",
                TransactionStatus.Pending,
                default
            )
        );

        Assert.Equal("timestamp", exception.ParamName);
    }

    [Fact]
    public void Create_WithTimestamp_PreservesItsValueAndOffset()
    {
        var timestamp = new DateTimeOffset(
            2026, 11, 6, 12, 0, 0,
            TimeSpan.FromHours(2)
        );

        var transaction = new Transaction(
            Guid.NewGuid(),
            100m,
            "USD",
            TransactionStatus.Pending,
            timestamp
        );

        Assert.Equal(timestamp, transaction.Timestamp);
        Assert.Equal(timestamp.Offset, transaction.Timestamp.Offset);
    }
}
