namespace Shop.Api.Models;

/// <summary>
/// Represents a promotional discount code that can be applied to an order.
/// </summary>
public sealed class Discount
{
    /// <summary>
    /// Gets or sets the unique identifier of the discount.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the discount code entered by the customer.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the discount percentage (0-100).
    /// </summary>
    public decimal Percent { get; set; }

    /// <summary>
    /// Gets or sets the expiration date in UTC, or <c>null</c> if the discount never expires.
    /// </summary>
    public DateTime? ValidToUtc { get; set; }

    /// <summary>
    /// Gets or sets the number of times the discount has been used.
    /// </summary>
    public int UsageCount { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of uses, or <c>null</c> for unlimited usage.
    /// </summary>
    public int? MaxUsages { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the discount is active.
    /// </summary>
    public bool IsActive { get; set; }
}
