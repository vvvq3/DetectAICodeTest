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

        public OrdersController(OrderService service)
        {
            _service = service;
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

        [HttpPost("{id}/cancel")]
        public async Task<IActionResult> Cancel(Guid id)
        {
            await _service.Cancel(id);
            return Ok();
        }
    }
}
