using Shop.Api.Models;

namespace Shop.Api.Services;

/// <summary>
/// Provides operations for managing and applying discount codes.
/// </summary>
public interface IDiscountService
{
    /// <summary>
    /// Applies the specified discount code to an order.
    /// </summary>
    /// <param name="order">The order to apply the discount to.</param>
    /// <param name="code">The discount code.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The result of applying the discount.</returns>
    Task<DiscountResult> ApplyDiscountAsync(Order order, string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all currently active discounts.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A read-only list of active discounts.</returns>
    Task<IReadOnlyList<Discount>> GetActiveDiscountsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deactivates the discount with the specified identifier.
    /// </summary>
    /// <param name="discountId">The discount identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task DeactivateAsync(int discountId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents the outcome of applying a discount to an order.
/// </summary>
/// <param name="IsApplied">Whether the discount was applied.</param>
/// <param name="NewTotal">The order total after the discount.</param>
/// <param name="Error">The reason the discount was not applied, if any.</param>
public sealed record DiscountResult(bool IsApplied, decimal NewTotal, string? Error = null);
