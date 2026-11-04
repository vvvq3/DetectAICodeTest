using System;

namespace Shop.Api.Models
{
    public class Discount
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public decimal Percent { get; set; }
        public DateTime? ValidTo { get; set; }
        public int UsedCnt { get; set; }
        public int MaxUses { get; set; }
        public bool Active { get; set; }
    }
}
