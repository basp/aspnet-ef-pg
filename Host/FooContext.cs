using Microsoft.EntityFrameworkCore;

namespace Host;

public class FooContext : DbContext
{
    public FooContext(DbContextOptions<FooContext> options)
        : base(options)
    {
    }

    public DbSet<Item> Items { get; set; }
}