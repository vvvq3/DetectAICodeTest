namespace Shop.Api.Options;

/// <summary>
/// Configuration options for discount processing.
/// </summary>
public sealed class DiscountOptions
{
    /// <summary>
    /// The configuration section name.
    /// </summary>
    public const string SectionName = "Discounts";

    /// <summary>
    /// Gets or sets the maximum allowed discount percentage.
    /// </summary>
    public decimal MaxPercent { get; set; } = 50m;
}
