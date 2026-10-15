using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TurboTest.EFCore;

Console.WriteLine("TurboTest Dynamic Scaling Example");
Console.WriteLine("=================================\n");

// Example 1: Enable dynamic scaling with defaults
Console.WriteLine("1. Dynamic scaling with default settings");
Console.WriteLine("----------------------------------------");

using var loggerFactory = LoggerFactory.Create(builder =>
    builder.AddConsole().SetMinimumLevel(LogLevel.Information));

var logger = loggerFactory.CreateLogger<Program>();

await using var fixture1 = await Tt.CreateFactoryAsync<BlogContext>(options =>
{
    options.AutoAllocateResources = true;
    options.EnableDynamicScaling = true;  // Enable dynamic scaling
    options.ScalingCheckIntervalSeconds = 10; // Check every 10 seconds for demo
    options.ReserveMemoryMb = 1024;
    options.Logger = logger;
});

await using var context1Lease = await fixture1.CreateLeaseAsync();
        var context1 = context1Lease.Context;
Console.WriteLine("✅ Context created with dynamic scaling enabled");
Console.WriteLine("   Monitoring system resources every 10 seconds...");

// Example 2: Custom scaling thresholds
Console.WriteLine("\n2. Custom scaling thresholds");
Console.WriteLine("----------------------------");

await using var fixture2 = await Tt.CreateFactoryAsync<BlogContext>(options =>
{
    options.AutoAllocateResources = true;
    options.EnableDynamicScaling = true;
    options.ScalingCheckIntervalSeconds = 15;
    
    // More aggressive scaling
    options.CpuScaleDownThreshold = 70;    // Scale down when CPU > 70%
    options.MemoryScaleDownThreshold = 75; // Scale down when memory > 75%
    options.CpuScaleUpThreshold = 20;      // Scale up when CPU < 20%
    options.MemoryScaleUpThreshold = 40;   // Scale up when memory < 40%
    
    options.MaxAutoContainers = 5; // Limit scaling to 5 containers
    options.Logger = logger;
});

await using var context2Lease = await fixture2.CreateLeaseAsync();
        var context2 = context2Lease.Context;
Console.WriteLine("✅ Context created with aggressive scaling thresholds");

// Example 3: Simulate load and observe scaling
Console.WriteLine("\n3. Simulating database load");
Console.WriteLine("---------------------------");

await using var context3Lease = await fixture2.CreateLeaseAsync();
        var context3 = context3Lease.Context;

// Create some test data and perform operations to potentially trigger scaling
for (int i = 0; i < 5; i++)
{
    Console.WriteLine($"   Creating test data batch {i + 1}...");
    
    var blog = new Blog { Title = $"Test Blog {i + 1}" };
    context3.Blogs.Add(blog);
    await context3.SaveChangesAsync();
    
    // Add some posts
    for (int j = 0; j < 3; j++)
    {
        context3.Posts.Add(new Post 
        { 
            Title = $"Post {j + 1} for Blog {i + 1}", 
            Content = "Test content",
            BlogId = blog.Id 
        });
    }
    await context3.SaveChangesAsync();
    
    // Wait to allow scaling checks
    await Task.Delay(2000);
}

Console.WriteLine("✅ Test data created - scaling system is monitoring resource usage");

// Example 4: Manual resource check (demonstration)
Console.WriteLine("\n4. Resource monitoring continues in background");
Console.WriteLine("----------------------------------------------");
Console.WriteLine("The scaling system will continue to monitor resources and adjust container count as needed.");
Console.WriteLine("Check the logs above for scaling decisions (scale up/down messages).");

// Wait a bit to see scaling in action
await Task.Delay(TimeSpan.FromSeconds(30));

Console.WriteLine("\n✨ Dynamic scaling example completed!");
Console.WriteLine("Note: Scaling continues until the application shuts down.");

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