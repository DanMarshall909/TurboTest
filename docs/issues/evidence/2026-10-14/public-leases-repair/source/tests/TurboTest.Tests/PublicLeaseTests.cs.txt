using Microsoft.EntityFrameworkCore;
using Docker.DotNet;
using Docker.DotNet.Models;
using Npgsql;
using TurboTest.EFCore;

namespace TurboTest.Tests;

public class PublicLeaseTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Three_sequential_public_leases_return_and_reuse_one_database()
    {
        try
        {
            await using var factory = await Tt.CreateFactoryAsync<TestDbContext>(options => options.PoolSize = 1);
            (string Database, uint Oid, int Session)? original = null;
            for (var i = 0; i < 3; i++)
            {
                await using var lease = await factory.CreateLeaseAsync();
                var context = lease.Context;
                var identity = await PooledResetTests.ReadIdentityAsync(context);
                if (original is null) original = identity;
                else Assert.Equal(original.Value, identity);
                Assert.Equal(0, await context.Users.CountAsync());
                context.Users.Add(new User { Email = $"public-lease-{i}@example.com" });
                await context.SaveChangesAsync();
            }
        }
        finally
        {
            await Tt.DisposeAsync();
        }
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Fixture_shutdown_closes_its_leases_and_rejects_new_acquisition()
    {
        try
        {
            await using var factory = await Tt.CreateFactoryAsync<TestDbContext>(o => o.PoolSize = 1);
            await using var lease = await factory.CreateLeaseAsync();
            await factory.DisposeAsync();
            Assert.Throws<ObjectDisposedException>(() => _ = lease.Context);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => factory.CreateLeaseAsync());
            await factory.DisposeAsync();
        }
        finally { await Tt.DisposeAsync(); }
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Constructor_failure_does_not_leave_an_undisclosed_database_lease()
    {
        var prefix = $"tt-constructor-{Guid.NewGuid():N}";
        using var docker = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
        await using var factory = await Tt.CreateFactoryAsync<ConstructorFailureContext>(o =>
        {
            o.PoolSize = 1;
            o.ContainerPrefix = prefix;
        });
        ConstructorFailureContext.Fail = true;
        try
        {
            var error = await Assert.ThrowsAsync<TtLifecycleException>(() => factory.CreateLeaseAsync());
            Assert.Equal("ContextCreate", error.Stage);
            Assert.DoesNotContain("constructor-secret", error.ToString());
            ConstructorFailureContext.Fail = false;
            await using var lease = await factory.CreateLeaseAsync();
            Assert.True(await lease.Context.Database.CanConnectAsync());
            var owned = await docker.Containers.ListContainersAsync(new ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["name"] = new Dictionary<string, bool> { [prefix] = true }
                }
            });
            Assert.Single(owned);
        }
        finally { ConstructorFailureContext.Fail = false; }
    }

    public sealed class ConstructorFailureContext : DbContext
    {
        public static bool Fail { get; set; }
        public ConstructorFailureContext(DbContextOptions<ConstructorFailureContext> options) : base(options)
        {
            if (Fail) throw new InvalidOperationException("constructor-secret");
        }
        public DbSet<User> Users => Set<User>();
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Cancellation_during_context_configuration_cannot_publish_a_lease()
    {
        await using var factory = await Tt.CreateFactoryAsync<TestDbContext>(o =>
        {
            o.PoolSize = 1;
            o.AutoMigrate = false;
        });
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => factory.CreateLeaseAsync(
            _ => cancellation.Cancel(), cancellation.Token));
        await using var next = await factory.CreateLeaseAsync();
        Assert.True(await next.Context.Database.CanConnectAsync());
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Multiple_contexts_and_sync_disposal_keep_the_lease_owner_until_reader_cleanup()
    {
        await using var factory = await Tt.CreateFactoryAsync<TestDbContext>(o => o.PoolSize = 1);
        await using var lease = await factory.CreateLeaseAsync();
        var identity = await PooledResetTests.ReadIdentityAsync(lease.Context);
        lease.Context.Users.Add(new User { Email = "shared-test@example.com" });
        await lease.Context.SaveChangesAsync();
        var second = lease.CreateContext();
        Assert.Equal("shared-test@example.com", (await second.Users.SingleAsync()).Email);
        lease.Context.Dispose();
        await using var transaction = await second.Database.BeginTransactionAsync();
        await using var command = ((NpgsqlConnection)second.Database.GetDbConnection()).CreateCommand();
        command.CommandText = "SELECT * FROM \"Users\"";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        await Task.WhenAll(lease.DisposeAsync().AsTask(), lease.DisposeAsync().AsTask());
        Assert.True(reader.IsClosed);
        await using var next = await factory.CreateLeaseAsync();
        Assert.Equal(identity, await PooledResetTests.ReadIdentityAsync(next.Context));
        Assert.Empty(await next.Context.Users.ToListAsync());
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Schema_initialization_failure_is_reported_before_a_lease_is_published()
    {
        await using var factory = await Tt.CreateFactoryAsync<InvalidModelContext>(o => o.PoolSize = 1);
        var error = await Assert.ThrowsAsync<TtLifecycleException>(() => factory.CreateLeaseAsync());
        Assert.Equal("ContextInitialize", error.Stage);
        Assert.DoesNotContain("schema-secret", error.ToString());
    }

    public sealed class InvalidModelContext(DbContextOptions<InvalidModelContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            throw new InvalidOperationException("schema-secret");
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Static_leases_reuse_then_recreate_a_valid_owner_after_final_disposal()
    {
        await Tt.DisposeAsync();
        Tt.Configure(o => { o.PoolSize = 1; o.AutoAllocateResources = false; });
        try
        {
            await using var first = await Tt.CreateLeaseAsync<TestDbContext>();
            var identity = await PooledResetTests.ReadIdentityAsync(first.Context);
            Assert.Throws<InvalidOperationException>(() => Tt.Configure(o => o.PoolSize = 2));
            await first.DisposeAsync();
            await using var second = await Tt.CreateLeaseAsync<TestDbContext>();
            Assert.Equal(identity, await PooledResetTests.ReadIdentityAsync(second.Context));
            await Tt.DisposeAsync();
            Assert.Throws<ObjectDisposedException>(() => _ = second.Context);
            await using var recreated = await Tt.CreateLeaseAsync<TestDbContext>();
            Assert.NotEqual(identity.Database, (await PooledResetTests.ReadIdentityAsync(recreated.Context)).Database);
        }
        finally { await Tt.DisposeAsync(); }
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Concurrent_fixtures_with_the_same_prefix_own_distinct_resources()
    {
        var prefix = $"tt-independent-{Guid.NewGuid():N}";
        TtFactory<TestDbContext>? first = null;
        TtFactory<TestDbContext>? other = null;
        try
        {
            var tasks = new[]
            {
                Tt.CreateFactoryAsync<TestDbContext>(o => { o.PoolSize = 1; o.ContainerPrefix = prefix; }),
                Tt.CreateFactoryAsync<TestDbContext>(o => { o.PoolSize = 1; o.ContainerPrefix = prefix; })
            };
            try { await Task.WhenAll(tasks); }
            finally
            {
                if (tasks[0].IsCompletedSuccessfully) first = await tasks[0];
                if (tasks[1].IsCompletedSuccessfully) other = await tasks[1];
            }
            await using var a = await first!.CreateLeaseAsync();
            await using var b = await other!.CreateLeaseAsync();
            Assert.NotEqual((await PooledResetTests.ReadIdentityAsync(a.Context)).Database,
                (await PooledResetTests.ReadIdentityAsync(b.Context)).Database);
            b.Context.Users.Add(new User { Email = "independent-owner@example.com" });
            await b.Context.SaveChangesAsync();
            await first.DisposeAsync();
            Assert.Equal("independent-owner@example.com", (await b.Context.Users.SingleAsync()).Email);
        }
        finally
        {
            if (first is not null) await first.DisposeAsync();
            if (other is not null) await other.DisposeAsync();
        }
    }
}
