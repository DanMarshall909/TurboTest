using Microsoft.EntityFrameworkCore;
using TurboTest.EFCore;

namespace TurboTest.Tests;

[Trait("Category", "PostgreSQL")]
public class BasicTests
{
    [Fact]
    public async Task Should_Create_Context_With_Real_Database()
    {
        await using var fixture = await Tt.CreateFactoryAsync<TestDbContext>(o => o.PoolSize = 2);
        // Act
        await using var contextLease = await fixture.CreateLeaseAsync();
        var context = contextLease.Context;

        // Assert
        context.ShouldNotBeNull();
        context.Database.ShouldNotBeNull();

        // Verify we can connect
        var canConnect = await context.Database.CanConnectAsync();
        canConnect.ShouldBeTrue();

        // Verify we can query
        var userCount = await context.Users.CountAsync();
        userCount.ShouldBe(0);
    }

    [Fact]
    public async Task Should_Enforce_Foreign_Key_Constraints()
    {
        await using var fixture = await Tt.CreateFactoryAsync<TestDbContext>(o => o.PoolSize = 2);
        // This test would pass with InMemory database!
        await using var contextLease = await fixture.CreateLeaseAsync();
        var context = contextLease.Context;

        // Try to add a post without a blog (violates FK)
        context.Posts.Add(new Post
        {
            Title = "Orphan Post",
            BlogId = 999 // Non-existent blog
        });

        // Act & Assert - Real database enforces FK constraint
        var act = () => context.SaveChangesAsync();
        var exception = await Should.ThrowAsync<DbUpdateException>(act);

        // Check that it's a proper database constraint error (EF Core wraps the original exception)
        var innerException = exception.InnerException.ShouldNotBeNull();
        innerException.Message.ShouldContain("violates foreign key constraint", Case.Sensitive);
    }

    [Fact]
    public async Task Should_Enforce_Unique_Constraints()
    {
        await using var fixture = await Tt.CreateFactoryAsync<TestDbContext>(o => o.PoolSize = 2);
        // This test would also pass incorrectly with InMemory database!
        await using var contextLease = await fixture.CreateLeaseAsync();
        var context = contextLease.Context;

        // Add two users with the same email
        context.Users.Add(new User { Email = "test@example.com", Name = "User 1" });
        context.Users.Add(new User { Email = "test@example.com", Name = "User 2" });

        // Act & Assert - Real database enforces unique constraint
        var act = () => context.SaveChangesAsync();
        var exception = await Should.ThrowAsync<DbUpdateException>(act);

        // Check that it's a proper database constraint error (EF Core wraps the original exception)
        var innerException = exception.InnerException.ShouldNotBeNull();
        innerException.Message.ShouldContain("duplicate key value violates unique constraint", Case.Sensitive);
    }

    [Fact]
    public async Task Should_Provide_Isolated_Databases()
    {
        await using var fixture = await Tt.CreateFactoryAsync<TestDbContext>(o => o.PoolSize = 2);
        // Create two contexts
        await using var context1Lease = await fixture.CreateLeaseAsync();
        var context1 = context1Lease.Context;
        await using var context2Lease = await fixture.CreateLeaseAsync();
        var context2 = context2Lease.Context;

        // Add data to first context
        context1.Users.Add(new User { Email = "user1@example.com" });
        await context1.SaveChangesAsync();

        // Second context should not see the data
        var count = await context2.Users.CountAsync();
        count.ShouldBe(0, "each test should get an isolated database");
    }

}
