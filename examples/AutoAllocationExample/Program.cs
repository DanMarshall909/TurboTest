using Microsoft.EntityFrameworkCore;
using TurboTest.EFCore;

Console.WriteLine("TurboTest Auto-Resource Allocation Example");
Console.WriteLine("=========================================\n");

// Example 1: Enable auto-allocation with defaults
Console.WriteLine("1. Auto-allocation with defaults");
Console.WriteLine("--------------------------------");

Tt.Configure(options =>
{
    options.AutoAllocateResources = true;
    // Reserves 2GB RAM and 2 CPU cores by default
    // Max 20 containers by default
});

await using var context1 = await Tt.CreateContextAsync<BlogContext>();
Console.WriteLine("✅ Context created with auto-allocated resources");
Console.WriteLine($"   Connection: {context1.Database.GetConnectionString()?.Split(';')[0]}");

// Example 2: Auto-allocation with custom reserves
Console.WriteLine("\n2. Auto-allocation with custom reserves");
Console.WriteLine("---------------------------------------");

Tt.Configure(options =>
{
    options.AutoAllocateResources = true;
    options.ReserveMemoryMb = 4096; // Reserve 4GB for system
    options.ReserveCpuCores = 1;    // Reserve 1 core for system
    options.MaxAutoContainers = 10; // Limit to 10 containers max
});

await using var context2 = await Tt.CreateContextAsync<BlogContext>();
Console.WriteLine("✅ Context created with custom resource reserves");

// Example 3: Compare manual vs auto configuration
Console.WriteLine("\n3. Manual vs Auto comparison");
Console.WriteLine("----------------------------");

// Manual configuration
Tt.Configure(options =>
{
    options.AutoAllocateResources = false;
    options.PoolSize = 3;
    options.MemoryLimitMb = 256;
});

await using var context3 = await Tt.CreateContextAsync<BlogContext>();
Console.WriteLine("✅ Manual config: 3 containers, 256MB each");

// Auto configuration for high-memory systems
Tt.Configure(options =>
{
    options.AutoAllocateResources = true;
    options.ReserveMemoryMb = 1024; // Only reserve 1GB
});

await using var context4 = await Tt.CreateContextAsync<BlogContext>();
Console.WriteLine("✅ Auto config: Optimized for available resources");

Console.WriteLine("\n✨ Auto-allocation examples completed!");

// Domain model for examples
public class Blog
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public List<Post> Posts { get; set; } = new();
}

public class Post
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public string? Content { get; set; }
    public int BlogId { get; set; }
    public Blog Blog { get; set; } = null!;
}

public class BlogContext : DbContext
{
    public DbSet<Blog> Blogs => Set<Blog>();
    public DbSet<Post> Posts => Set<Post>();

    public BlogContext(DbContextOptions<BlogContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Post>()
            .HasOne(p => p.Blog)
            .WithMany(b => b.Posts)
            .HasForeignKey(p => p.BlogId);
    }
}