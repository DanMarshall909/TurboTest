using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TurboTest.EFCore;

Console.WriteLine("TurboTest Logging Example");
Console.WriteLine("========================\n");

// Example 1: Basic logging with console
Console.WriteLine("1. Basic logging with console output");
Console.WriteLine("------------------------------------");

using var loggerFactory = LoggerFactory.Create(builder =>
    builder.AddConsole().SetMinimumLevel(LogLevel.Debug));

var logger = loggerFactory.CreateLogger<Program>();

await using var fixture1 = await Tt.CreateFactoryAsync<BlogContext>(options =>
{
    options.AutoAllocateResources = true;
    options.ReserveMemoryMb = 1024; // Reserve 1GB for demo
    options.LogLevel = LogLevel.Debug;
    options.Logger = logger;
});

await using var context1Lease = await fixture1.CreateLeaseAsync();
var context1 = context1Lease.Context;
Console.WriteLine("✅ Context created with detailed logging");

// Example 2: Production-style logging (minimal)
Console.WriteLine("\n2. Production-style logging");
Console.WriteLine("---------------------------");

using var prodLoggerFactory = LoggerFactory.Create(builder =>
    builder.AddConsole().SetMinimumLevel(LogLevel.Information));

var prodLogger = prodLoggerFactory.CreateLogger("TurboTest");

await using var fixture2 = await Tt.CreateFactoryAsync<BlogContext>(options =>
{
    options.AutoAllocateResources = false;
    options.PoolSize = 2;
    options.MemoryLimitMb = 256;
    options.LogLevel = LogLevel.Information;
    options.Logger = prodLogger;
});

await using var context2Lease = await fixture2.CreateLeaseAsync();
var context2 = context2Lease.Context;
Console.WriteLine("✅ Context created with production logging");

// Example 3: Custom logging for specific scenarios
Console.WriteLine("\n3. Custom resource allocation logging");
Console.WriteLine("-------------------------------------");

using var customLoggerFactory = LoggerFactory.Create(builder =>
    builder
        .AddConsole()
        .AddFilter("TurboTest", LogLevel.Information)
        .SetMinimumLevel(LogLevel.Warning));

var customLogger = customLoggerFactory.CreateLogger("TurboTest");

await using var fixture3 = await Tt.CreateFactoryAsync<BlogContext>(options =>
{
    options.AutoAllocateResources = true;
    options.ReserveMemoryMb = 512;   // Reserve less memory
    options.ReserveCpuCores = 1;     // Reserve fewer cores
    options.MaxAutoContainers = 8;   // Limit containers
    options.LogLevel = LogLevel.Debug;
    options.Logger = customLogger;
});

await using var context3Lease = await fixture3.CreateLeaseAsync();
var context3 = context3Lease.Context;
Console.WriteLine("✅ Context created with custom logging configuration");

Console.WriteLine("\n✨ All logging examples completed!");

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