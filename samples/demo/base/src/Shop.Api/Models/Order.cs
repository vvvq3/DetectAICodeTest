using System;
using System.Collections.Generic;

namespace Shop.Api.Models
{
    public class Order
    {
        public Guid Id { get; set; }
        public string CustomerEmail { get; set; }
        public DateTime Created { get; set; }
        public decimal Total { get; set; }
        public string Status { get; set; }
        public List<OrderItem> Items { get; set; } = new List<OrderItem>();
    }

    public class OrderItem
    {
        public int Id { get; set; }
        public string Sku { get; set; }
        public int Qty { get; set; }
        public decimal Price { get; set; }
    }
}
