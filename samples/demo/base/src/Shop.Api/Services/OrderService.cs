using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shop.Api.Data;
using Shop.Api.Models;

namespace Shop.Api.Services
{
    public class OrderService
    {
        private ShopDbContext _db;
        private readonly ILogger<OrderService> _logger;

        public OrderService(ShopDbContext db, ILogger<OrderService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<Order> GetOrder(Guid id)
        {
            var o = await _db.Orders.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id);
            if (o == null) throw new Exception($"order {id} not found"); // TODO: свой тип исключения
            return o;
        }

        public async Task<Order> Create(string email, OrderItem[] items)
        {
            var order = new Order();
            order.Id = Guid.NewGuid();
            order.CustomerEmail = email;
            order.Created = DateTime.Now;
            order.Status = "new";
            order.Items = items.ToList();
            order.Total = items.Sum(i => i.Price * i.Qty);
            _db.Orders.Add(order);
            await _db.SaveChangesAsync();
            _logger.LogInformation($"Created order {order.Id} for {email}");
            return order;
        }

        public async Task Cancel(Guid id)
        {
            var o = await GetOrder(id);
            //if (o.Status == "shipped") return;
            o.Status = "cancelled";
            await _db.SaveChangesAsync();
        }
    }
}
