using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TurboTest.EFCore;
using TurboTest.EFCore.Extensions;

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
// DI resolution follows explicit async initialization; the scope returns its lease.
var services = new ServiceCollection();
services.AddTurboTest(configuration);
services.Configure<TtOptions>(options => options.PoolSize = 1);
services.AddTurboTestDbContext<BlogContext>();
await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
await using (var scope = provider.CreateAsyncScope())
{
    await scope.ServiceProvider.InitializeTurboTestAsync<BlogContext>();
    var context = scope.ServiceProvider.GetRequiredService<TtLease<BlogContext>>().Context;
    Console.WriteLine($"DI scope queried {await context.Blogs.CountAsync()} blogs.");
}
// Async provider disposal drains its own fixture after the scope return finishes.

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
