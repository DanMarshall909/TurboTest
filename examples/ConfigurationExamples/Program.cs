using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TurboTest.EFCore;
using TurboTest.EFCore.Extensions;

Console.WriteLine("TurboTest Configuration Examples");
Console.WriteLine("=================================\n");

// Example 1: Configuration from appsettings.json
Console.WriteLine("1. Configuration from appsettings.json");
Console.WriteLine("--------------------------------------");

var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .AddEnvironmentVariables()
    .Build();

// Configure TurboTest globally from appsettings.json
Tt.Configure(configuration);

await using var context1 = await Tt.CreateContextAsync<BlogContext>();
Console.WriteLine("✅ Context created with appsettings.json configuration");
Console.WriteLine($"   Connection: {context1.Database.GetConnectionString()?.Split(';')[0]}");

// Example 2: Programmatic configuration
Console.WriteLine("\n2. Programmatic configuration");
Console.WriteLine("-----------------------------");

Tt.Configure(options =>
{
    options.PoolSize = 2;
    options.MemoryLimitMb = 256;
    options.EnableSensitiveDataLogging = true;
    options.ContainerPrefix = "example";
});

await using var context2 = await Tt.CreateContextAsync<BlogContext>();
Console.WriteLine("✅ Context created with programmatic configuration");

// Example 3: Dependency Injection (like ASP.NET Core)
Console.WriteLine("\n3. Dependency Injection pattern");
Console.WriteLine("-------------------------------");

var services = new ServiceCollection();

// Register TurboTest with configuration
services.AddTurboTest(configuration);

// Or with programmatic config
services.AddTurboTest(options =>
{
    options.PoolSize = 3;
    options.AutoMigrate = true;
});

// Register your DbContext to use TurboTest
services.AddTurboTestDbContext<BlogContext>(options =>
{
    options.EnableSensitiveDataLogging();
});

var serviceProvider = services.BuildServiceProvider();

// Use through DI
var factory = serviceProvider.GetRequiredService<ITtServiceFactory>();
await using var context3 = await factory.CreateContextAsync<BlogContext>();
Console.WriteLine("✅ Context created through dependency injection");

// Example 4: Test fixture pattern
Console.WriteLine("\n4. Test fixture pattern");
Console.WriteLine("-----------------------");

await using var testFixture = await Tt.CreateFactoryAsync<BlogContext>(options =>
{
    options.AutoMigrate = true;
    options.EnableSensitiveDataLogging = false;
});

await using var context4 = await testFixture.CreateContextAsync();
await using var context5 = await testFixture.CreateContextAsync();
Console.WriteLine("✅ Multiple contexts created from factory");
Console.WriteLine("   Each gets its own isolated database");

// Example 5: Environment-based configuration
Console.WriteLine("\n5. Environment variables");
Console.WriteLine("------------------------");

Environment.SetEnvironmentVariable("TurboTest__PoolSize", "1");
Environment.SetEnvironmentVariable("TurboTest__MemoryLimitMb", "128");

var envConfig = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .Build();

Tt.Configure(envConfig);
await using var context6 = await Tt.CreateContextAsync<BlogContext>();
Console.WriteLine("✅ Context created with environment variable configuration");

Console.WriteLine("\n✨ All configuration examples completed!");

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