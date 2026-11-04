using Microsoft.EntityFrameworkCore;
using Shop.Api.Models;

namespace Shop.Api.Data
{
    public class ShopDbContext : DbContext
    {
        public ShopDbContext(DbContextOptions<ShopDbContext> options) : base(options) { }

        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>().HasMany(o => o.Items).WithOne();
            // цены храним с 2 знаками
            modelBuilder.Entity<OrderItem>().Property(x => x.Price).HasPrecision(18, 2);
        }
    }
}
