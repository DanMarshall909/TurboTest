using Microsoft.EntityFrameworkCore;
using TurboTest.EFCore;

namespace TurboTest.Tests;

public class BasicTests
{
    [Fact]
    public async Task Should_Create_Context_With_Real_Database()
    {
        // Act
        await using var context = await Tt.CreateContextAsync<TestDbContext>();

        // Assert
        context.Should().NotBeNull();
        context.Database.Should().NotBeNull();

        // Verify we can connect
        var canConnect = await context.Database.CanConnectAsync();
        canConnect.Should().BeTrue();

        // Verify we can query
        var userCount = await context.Users.CountAsync();
        userCount.Should().Be(0);
    }

    [Fact]
    public async Task Should_Enforce_Foreign_Key_Constraints()
    {
        // This test would pass with InMemory database!
        await using var context = await Tt.CreateContextAsync<TestDbContext>();

        // Try to add a post without a blog (violates FK)
        context.Posts.Add(new Post
        {
            Title = "Orphan Post",
            BlogId = 999 // Non-existent blog
        });

        // Act & Assert - Real database enforces FK constraint
        var act = () => context.SaveChangesAsync();
        var exception = await act.Should().ThrowAsync<DbUpdateException>();
        
        // Check that it's a proper database constraint error (EF Core wraps the original exception)
        exception.Which.InnerException.Should().NotBeNull();
        exception.Which.InnerException!.Message.Should().Contain("violates foreign key constraint");
    }

    [Fact]
    public async Task Should_Enforce_Unique_Constraints()
    {
        // This test would also pass incorrectly with InMemory database!
        await using var context = await Tt.CreateContextAsync<TestDbContext>();

        // Add two users with the same email
        context.Users.Add(new User { Email = "test@example.com", Name = "User 1" });
        context.Users.Add(new User { Email = "test@example.com", Name = "User 2" });

        // Act & Assert - Real database enforces unique constraint
        var act = () => context.SaveChangesAsync();
        var exception = await act.Should().ThrowAsync<DbUpdateException>();
        
        // Check that it's a proper database constraint error (EF Core wraps the original exception)
        exception.Which.InnerException.Should().NotBeNull();
        exception.Which.InnerException!.Message.Should().Contain("duplicate key value violates unique constraint");
    }

    [Fact]
    public async Task Should_Provide_Isolated_Databases()
    {
        // Create two contexts
        await using var context1 = await Tt.CreateContextAsync<TestDbContext>();
        await using var context2 = await Tt.CreateContextAsync<TestDbContext>();

        // Add data to first context
        context1.Users.Add(new User { Email = "user1@example.com" });
        await context1.SaveChangesAsync();

        // Second context should not see the data
        var count = await context2.Users.CountAsync();
        count.Should().Be(0, "each test should get an isolated database");
    }

    [Fact]
    public async Task Should_Reset_Database_Quickly()
    {
        var stopwatch = new System.Diagnostics.Stopwatch();

        // First acquisition (might be slower)
        await using var context1 = await Tt.CreateContextAsync<TestDbContext>();
        context1.Users.Add(new User { Email = "test@example.com" });
        await context1.SaveChangesAsync();
        await context1.DisposeAsync();

        // Second acquisition (should be fast due to reset)
        stopwatch.Start();
        await using var context2 = await Tt.CreateContextAsync<TestDbContext>();
        stopwatch.Stop();

        // Database should be clean
        var count = await context2.Users.CountAsync();
        count.Should().Be(0);

        // Reset should be fast
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(500,
            "database reset should be quick");
    }
}