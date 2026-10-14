using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TurboTest.EFCore;

var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json").AddEnvironmentVariables().Build();
await using var configured = await Tt.CreateFactoryAsync<BlogContext>(options =>
    configuration.GetSection(TtOptions.SectionName).Bind(options));
await using (var lease = await configured.CreateLeaseAsync())
{
    Console.WriteLine($"Configured fixture connected: {await lease.Context.Database.CanConnectAsync()}");
}

await using var fixture = await Tt.CreateFactoryAsync<BlogContext>(options =>
{
    options.PoolSize = 1;
    options.EnableSensitiveDataLogging = false;
});
await using (var lease = await fixture.CreateLeaseAsync())
{
    var context = lease.Context;
    var secondContext = lease.CreateContext();
    Console.WriteLine($"Both contexts share the test database: {await secondContext.Database.CanConnectAsync()}");
}
// Lease return finishes before fixture shutdown. DI registration/lifetimes are tracked in TT-007.

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