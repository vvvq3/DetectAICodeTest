using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shop.Api.Data;
using Shop.Api.Models;
using Shop.Api.Options;

namespace Shop.Api.Services;

/// <inheritdoc />
public sealed class DiscountService : IDiscountService
{
    private readonly ShopDbContext _dbContext;
    private readonly ILogger<DiscountService> _logger;
    private readonly DiscountOptions _options;
    private readonly TimeProvider _timeProvider;

    public DiscountService(
        ShopDbContext dbContext,
        ILogger<DiscountService> logger,
        IOptions<DiscountOptions> options,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <inheritdoc />
    public async Task<DiscountResult> ApplyDiscountAsync(Order order, string code, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var normalizedCode = code.Trim().ToUpperInvariant();

        var discount = await _dbContext.Set<Discount>()
            .FirstOrDefaultAsync(d => d.Code == normalizedCode, cancellationToken);

        if (discount is null)
        {
            _logger.LogWarning("Discount code {DiscountCode} was not found", normalizedCode);
            return new DiscountResult(false, order.Total, "Discount code not found.");
        }

        if (!IsDiscountValid(discount))
        {
            _logger.LogWarning("Discount code {DiscountCode} is expired or exhausted", normalizedCode);
            return new DiscountResult(false, order.Total, "Discount code is no longer valid.");
        }

        // Cap the percentage to the configured maximum
        var effectivePercent = Math.Min(discount.Percent, _options.MaxPercent);
        var discountAmount = Math.Round(order.Total * effectivePercent / 100m, 2, MidpointRounding.AwayFromZero);
        var newTotal = order.Total - discountAmount;

        discount.UsageCount++;
        order.Total = newTotal;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Applied discount {DiscountCode} to order {OrderId}. New total: {NewTotal}",
            normalizedCode,
            order.Id,
            newTotal);

        return new DiscountResult(true, newTotal);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Discount>> GetActiveDiscountsAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        return await _dbContext.Set<Discount>()
            .AsNoTracking()
            .Where(d => d.IsActive && (d.ValidToUtc == null || d.ValidToUtc > now))
            .OrderBy(d => d.Code)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeactivateAsync(int discountId, CancellationToken cancellationToken = default)
    {
        var discount = await _dbContext.Set<Discount>().FindAsync([discountId], cancellationToken)
            ?? throw new KeyNotFoundException($"Discount with ID '{discountId}' was not found.");

        discount.IsActive = false;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deactivated discount {DiscountId}", discountId);
    }

    private bool IsDiscountValid(Discount discount)
    {
        if (!discount.IsActive)
        {
            return false;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (discount.ValidToUtc is not null && discount.ValidToUtc <= now)
        {
            return false;
        }

        return discount.MaxUsages is null || discount.UsageCount < discount.MaxUsages;
    }
}
