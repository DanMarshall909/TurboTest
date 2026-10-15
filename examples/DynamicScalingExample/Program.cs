using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TurboTest.EFCore;

using var loggerFactory = LoggerFactory.Create(builder =>
    builder.AddConsole().SetMinimumLevel(LogLevel.Debug));

// One host, a sealed schema/seed baseline, and at most eight workload databases.
// Adaptive defaults favor sustained pressure; the first burst may queue while
// growth verifies new databases. Already-warm fixed capacity can be faster.
await using var fixture = await Tt.CreateFactoryAsync(
    new TtBaselineDefinition<BlogContext>("blogs-v1", TtSchemaInitialization.EnsureCreated), options =>
    {
        options.PoolSize = 8;
        options.StartupWarmDatabases = 2;
        options.EnableDynamicScaling = true;
        options.AdaptivePool = new() { MinimumWarmDatabases = 2, MaximumWarmDatabases = 8 };
        options.Logger = loggerFactory.CreateLogger("AdaptiveExample");
        options.LogLevel = LogLevel.Debug;
    });

using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
foreach (var concurrency in new[] { 1, 8, 1 })
{
    await Task.WhenAll(Enumerable.Range(0, concurrency).Select(async worker =>
    {
        await using var lease = await fixture.CreateLeaseAsync(cancellationToken: deadline.Token);
        lease.Context.Blogs.Add(new Blog { Title = $"Worker {worker}" });
        await lease.Context.SaveChangesAsync(deadline.Token);
        await Task.Delay(TimeSpan.FromSeconds(3), deadline.Token);
    }));
    Console.WriteLine($"After {concurrency} workers: target {fixture.PoolState.WarmTarget}, ready {fixture.PoolState.Ready}, parked {fixture.PoolState.Parked}");
}

// Shrink requires a full spare window per step. Retained databases continue to
// consume physical reservations; renewed demand verifies them before reuse.
await Task.Delay(TimeSpan.FromSeconds(15), deadline.Token);
Console.WriteLine($"After idle: target {fixture.PoolState.WarmTarget}, ready {fixture.PoolState.Ready}, parked {fixture.PoolState.Parked}");
await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
{
    await using var lease = await fixture.CreateLeaseAsync(cancellationToken: deadline.Token);
    Console.WriteLine($"Restored blogs: {await lease.Context.Blogs.CountAsync(deadline.Token)}");
}));
// Awaited fixture disposal stops monitoring and joins admitted work before host deletion.

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