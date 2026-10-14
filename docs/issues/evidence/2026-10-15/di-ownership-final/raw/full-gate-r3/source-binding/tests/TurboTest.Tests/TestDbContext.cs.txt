using Microsoft.EntityFrameworkCore;

namespace TurboTest.Tests;

// Test models
public class Blog
{
    public int Id { get; set; }
    public required string Url { get; set; }
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

public class User
{
    public int Id { get; set; }
    public required string Email { get; set; }
    public string? Name { get; set; }
}

// Test DbContext
public class TestDbContext : DbContext
{
    public DbSet<Blog> Blogs => Set<Blog>();
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<User> Users => Set<User>();

    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configure relationships
        modelBuilder.Entity<Post>()
            .HasOne(p => p.Blog)
            .WithMany(b => b.Posts)
            .HasForeignKey(p => p.BlogId)
            .OnDelete(DeleteBehavior.Cascade);

        // Add unique constraint
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();
    }
}