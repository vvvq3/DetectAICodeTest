using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shop.Api.Data;
using Shop.Api.Models;

namespace Shop.Api.Services
{
    public class DiscountService
    {
        private ShopDbContext _db;
        private ILogger<DiscountService> _log;

        // максимальная скидка, больше нельзя по требованию бизнеса
        private const decimal MAX_PERCENT = 50;

        public DiscountService(ShopDbContext db, ILogger<DiscountService> log)
        {
            _db = db;
            _log = log;
        }

        public async Task<Discount> Find(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            var d = await _db.Set<Discount>().FirstOrDefaultAsync(x => x.Code == code.ToUpper());
            return d;
        }

        // применяем скидку к заказу, возвращаем новую сумму
        public async Task<decimal> Apply(Order order, string code)
        {
            var d = await Find(code);
            if (d == null)
            {
                _log.LogWarning("discount not found: " + code);
                return order.Total;
            }
            if (!d.Active || (d.ValidTo != null && d.ValidTo < DateTime.Now))
            {
                _log.LogWarning($"discount {code} expired");
                return order.Total;
            }
            if (d.MaxUses > 0 && d.UsedCnt >= d.MaxUses)
                return order.Total;

            var pct = Math.Min(d.Percent, MAX_PERCENT);
            var sum = order.Total - order.Total * pct / 100;
            sum = Math.Round(sum, 2);

            d.UsedCnt++;
            order.Total = sum;
            await _db.SaveChangesAsync();
            _log.LogInformation($"applied {code} to order {order.Id}, new total {sum}");
            return sum;
        }

        public List<Discount> GetActive()
        {
            // TODO: кэшировать, дергается на каждой странице
            var lst = _db.Set<Discount>().Where(x => x.Active).ToList();
            var res = new List<Discount>();
            foreach (var d in lst)
            {
                if (d.ValidTo == null || d.ValidTo > DateTime.Now)
                    res.Add(d);
            }
            return res;
        }

        public async Task Deactivate(int id)
        {
            var d = await _db.Set<Discount>().FindAsync(id);
            if (d == null) throw new Exception("discount " + id + " not found");
            d.Active = false;
            await _db.SaveChangesAsync();
            //_log.LogInformation("deactivated " + id);
        }
    }
}
