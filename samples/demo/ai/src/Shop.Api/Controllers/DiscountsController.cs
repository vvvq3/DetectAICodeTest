using Microsoft.AspNetCore.Mvc;
using Shop.Api.Models;
using Shop.Api.Services;

namespace Shop.Api.Controllers;

/// <summary>
/// Exposes endpoints for working with discount codes.
/// </summary>
[ApiController]
[Route("api/discounts")]
public sealed class DiscountsController : ControllerBase
{
    private readonly IDiscountService _discountService;
    private readonly OrderService _orderService;

    public DiscountsController(IDiscountService discountService, OrderService orderService)
    {
        _discountService = discountService;
        _orderService = orderService;
    }

    /// <summary>
    /// Gets all active discounts.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<Discount>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActive(CancellationToken cancellationToken)
    {
        var discounts = await _discountService.GetActiveDiscountsAsync(cancellationToken);
        return Ok(discounts);
    }

    /// <summary>
    /// Applies a discount code to the specified order.
    /// </summary>
    /// <param name="orderId">The order identifier.</param>
    /// <param name="request">The discount request.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    [HttpPost("/api/orders/{orderId:guid}/discount")]
    [ProducesResponseType(typeof(DiscountResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Apply(Guid orderId, [FromBody] ApplyDiscountRequest request, CancellationToken cancellationToken)
    {
        var order = await _orderService.GetOrder(orderId);

        var result = await _discountService.ApplyDiscountAsync(order, request.Code, cancellationToken);
        if (!result.IsApplied)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result);
    }

    /// <summary>
    /// Deactivates a discount.
    /// </summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken)
    {
        await _discountService.DeactivateAsync(id, cancellationToken);
        return NoContent();
    }
}

/// <summary>
/// Request payload for applying a discount code.
/// </summary>
/// <param name="Code">The discount code.</param>
public sealed record ApplyDiscountRequest(string Code);
