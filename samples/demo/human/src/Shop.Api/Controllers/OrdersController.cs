using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Shop.Api.Models;
using Shop.Api.Services;

namespace Shop.Api.Controllers
{
    [ApiController]
    [Route("api/orders")]
    public class OrdersController : ControllerBase
    {
        private readonly OrderService _service;
        private readonly DiscountService _discounts;

        public OrdersController(OrderService service, DiscountService discounts)
        {
            _service = service;
            _discounts = discounts;
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> Get(Guid id)
        {
            try
            {
                var order = await _service.GetOrder(id);
                return Ok(order);
            }
            catch (Exception e)
            {
                return NotFound(e.Message);
            }
        }

        [HttpPost]
        public async Task<IActionResult> Create(string email, [FromBody] OrderItem[] items)
        {
            var res = await _service.Create(email, items);
            return Ok(res);
        }

        [HttpPost("{id}/discount")]
        public async Task<IActionResult> ApplyDiscount(Guid id, string code)
        {
            var order = await _service.GetOrder(id);
            if (order.Status != "new")
                return BadRequest("заказ уже оформлен");
            var total = await _discounts.Apply(order, code);
            return Ok(new { order.Id, total });
        }

        [HttpGet("discounts")]
        public IActionResult Discounts()
        {
            return Ok(_discounts.GetActive());
        }

        [HttpPost("{id}/cancel")]
        public async Task<IActionResult> Cancel(Guid id)
        {
            await _service.Cancel(id);
            return Ok();
        }
    }
}
