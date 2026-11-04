using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shop.Api.Data;
using Shop.Api.Models;
using Shop.Api.Options;
using Shop.Api.Services;
using Xunit;

namespace Shop.Api.Tests.Services;

public class DiscountServiceTests : IDisposable
{
    private readonly ShopDbContext _dbContext;
    private readonly FakeTimeProvider _timeProvider;
    private readonly DiscountService _sut;

    public DiscountServiceTests()
    {
        var options = new DbContextOptionsBuilder<ShopDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ShopDbContext(options);
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2025, 1, 15, 12, 0, 0, TimeSpan.Zero));
        _sut = new DiscountService(
            _dbContext,
            NullLogger<DiscountService>.Instance,
            Microsoft.Extensions.Options.Options.Create(new DiscountOptions { MaxPercent = 50m }),
            _timeProvider);
    }

    [Fact]
    public async Task ApplyDiscountAsync_WithValidCode_ReducesOrderTotal()
    {
        // Arrange
        await SeedDiscountAsync(new Discount { Code = "SAVE10", Percent = 10m, IsActive = true });
        var order = new Order { Id = Guid.NewGuid(), CustomerEmail = "test@example.com", Total = 200m };

        // Act
        var result = await _sut.ApplyDiscountAsync(order, "save10");

        // Assert
        result.IsApplied.Should().BeTrue();
        result.NewTotal.Should().Be(180m);
        order.Total.Should().Be(180m);
    }

    [Fact]
    public async Task ApplyDiscountAsync_WithUnknownCode_ReturnsNotApplied()
    {
        // Arrange
        var order = new Order { Id = Guid.NewGuid(), Total = 100m };

        // Act
        var result = await _sut.ApplyDiscountAsync(order, "UNKNOWN");

        // Assert
        result.IsApplied.Should().BeFalse();
        result.NewTotal.Should().Be(100m);
    }

    [Fact]
    public async Task ApplyDiscountAsync_WithExpiredCode_ReturnsNotApplied()
    {
        // Arrange
        await SeedDiscountAsync(new Discount
        {
            Code = "OLD",
            Percent = 20m,
            IsActive = true,
            ValidToUtc = _timeProvider.GetUtcNow().UtcDateTime.AddDays(-1),
        });
        var order = new Order { Id = Guid.NewGuid(), Total = 100m };

        // Act
        var result = await _sut.ApplyDiscountAsync(order, "OLD");

        // Assert
        result.IsApplied.Should().BeFalse();
    }

    [Theory]
    [InlineData(80, 50)]
    [InlineData(50, 50)]
    [InlineData(30, 70)]
    public async Task ApplyDiscountAsync_CapsPercentAtConfiguredMaximum(decimal percent, decimal expectedTotal)
    {
        // Arrange
        await SeedDiscountAsync(new Discount { Code = "BIG", Percent = percent, IsActive = true });
        var order = new Order { Id = Guid.NewGuid(), Total = 100m };

        // Act
        var result = await _sut.ApplyDiscountAsync(order, "BIG");

        // Assert
        result.NewTotal.Should().Be(expectedTotal);
    }

    [Fact]
    public async Task ApplyDiscountAsync_WithNullOrder_ThrowsArgumentNullException()
    {
        // Act
        var act = () => _sut.ApplyDiscountAsync(null!, "CODE");

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    private async Task SeedDiscountAsync(Discount discount)
    {
        _dbContext.Set<Discount>().Add(discount);
        await _dbContext.SaveChangesAsync();
    }

    public void Dispose() => _dbContext.Dispose();
}
