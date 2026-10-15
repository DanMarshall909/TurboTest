using Microsoft.EntityFrameworkCore;
using Npgsql;
using Microsoft.Extensions.Logging;
using Docker.DotNet;
using Docker.DotNet.Models;
using TurboTest.EFCore;
using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

public class PooledResetTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Repeated_factory_returns_reuse_the_database_and_its_warm_session()
    {
        await using var pool = new TtPool(new TtOptions { PoolSize = 1 });
        await pool.InitializeAsync();
        await using var factory = new TtFactory<TestDbContext>(pool, new TtOptions());
        (string Database, uint Oid, int Session)? original = null;

        for (var iteration = 0; iteration < 3; iteration++)
        {
            var contextLease = await factory.CreateLeaseAsync();
            var context = contextLease.Context;
            var identity = await ReadIdentityAsync(context);
            if (original is null) original = identity;
            else Assert.Equal(original.Value, identity);
            Assert.Equal(0, await context.Users.CountAsync());
            context.Users.Add(new User { Email = $"iteration-{iteration}@example.com" });
            await context.SaveChangesAsync();
            // Returning the public lease awaits cleanup before the next acquisition.
            await contextLease.DisposeAsync();
        }
    }

    internal static async Task<(string Database, uint Oid, int Session)> ReadIdentityAsync(DbContext context)
    {
        await context.Database.OpenConnectionAsync();
        try
        {
            await using var command = new NpgsqlCommand(
                "SELECT current_database(), oid, pg_backend_pid() FROM pg_database WHERE datname = current_database()",
                (NpgsqlConnection)context.Database.GetDbConnection());
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            return (reader.GetString(0), reader.GetFieldValue<uint>(1), reader.GetInt32(2));
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Failed_reset_reports_its_stage_and_does_not_publish_the_dirty_database()
    {
        var logger = new RecordingLogger();
        await using var pool = new TtPool(new TtOptions { PoolSize = 1, Logger = logger });
        await pool.InitializeAsync();
        var database = await pool.GetDatabaseAsync();
        await using (var connection = await database.DataSource.OpenConnectionAsync())
        {
            await using var command = new NpgsqlCommand("""
                CREATE TABLE broken_reset (id integer);
                CREATE FUNCTION reject_reset() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'secret-test-payload'; END $$;
                CREATE TRIGGER reject_reset BEFORE TRUNCATE ON broken_reset
                FOR EACH STATEMENT EXECUTE FUNCTION reject_reset();
                """, connection);
            await command.ExecuteNonQueryAsync();
        }

        var error = await Assert.ThrowsAsync<TtLifecycleException>(
            () => database.DisposeAsync().AsTask());
        Assert.Equal("ResetRows", error.Stage);
        Assert.Equal("P0001", error.SqlState);
        Assert.DoesNotContain("secret-test-payload", error.ToString());
        Assert.DoesNotContain("secret-test-payload", string.Join('\n', logger.Messages));
        Assert.DoesNotContain("Password=", string.Join('\n', logger.Messages));
        var replacement = await pool.GetDatabaseAsync();
        Assert.NotEqual(DatabaseName(database), DatabaseName(replacement));
        await replacement.DisposeAsync();
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Failed_lease_cleanup_is_reported_and_cannot_return_a_resource_to_the_pool()
    {
        await using var pool = new TtPool(new TtOptions { PoolSize = 1 });
        await pool.InitializeAsync();
        var database = await pool.GetDatabaseAsync();
        database.OnDispose(() => Task.FromException(new IOException("secret-test-payload")));
        var remainingCallbackRan = false;
        database.OnDispose(() => { remainingCallbackRan = true; return Task.CompletedTask; });

        var error = await Assert.ThrowsAsync<TtLifecycleException>(() => database.DisposeAsync().AsTask());
        Assert.Equal("LeaseCleanup", error.Stage);
        Assert.DoesNotContain("secret-test-payload", error.ToString());
        Assert.True(remainingCallbackRan);
        var replacement = await pool.GetDatabaseAsync();
        Assert.NotEqual(DatabaseName(database), DatabaseName(replacement));
        await replacement.DisposeAsync();
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }

    private static string? DatabaseName(TtDatabase database) =>
        new NpgsqlConnectionStringBuilder(database.DataSource.ConnectionString).Database;

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Return_closes_an_outstanding_reader_and_transaction_before_reset()
    {
        await using var pool = new TtPool(new TtOptions { PoolSize = 1 });
        await pool.InitializeAsync();
        await using var factory = new TtFactory<TestDbContext>(pool, new TtOptions());
        var contextLease = await factory.CreateLeaseAsync();
        var context = contextLease.Context;
        var original = await ReadIdentityAsync(context);
        await context.Database.OpenConnectionAsync();
        await using var transaction = await context.Database.BeginTransactionAsync();
        await using var command = ((NpgsqlConnection)context.Database.GetDbConnection()).CreateCommand();
        command.CommandText = "INSERT INTO \"Users\" (\"Email\") VALUES ('uncommitted@example.com'); SELECT 1 UNION ALL SELECT 2";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.False(reader.IsClosed);

        await contextLease.DisposeAsync();
        Assert.True(reader.IsClosed);
        var nextLease = await factory.CreateLeaseAsync();
        var next = nextLease.Context;
        Assert.Equal(original, await ReadIdentityAsync(next));
        Assert.Equal(0, await next.Users.CountAsync());
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Reset_and_disposal_leave_another_fixture_session_and_rows_untouched()
    {
        await using var firstPool = new TtPool(new TtOptions { PoolSize = 1 });
        await using var otherPool = new TtPool(new TtOptions { PoolSize = 1 });
        await firstPool.InitializeAsync();
        await otherPool.InitializeAsync();
        await using var first = new TtFactory<TestDbContext>(firstPool, new TtOptions());
        await using var other = new TtFactory<TestDbContext>(otherPool, new TtOptions());
        var otherContextLease = await other.CreateLeaseAsync();
        var otherContext = otherContextLease.Context;
        var otherIdentity = await ReadIdentityAsync(otherContext);
        otherContext.Users.Add(new User { Email = "unrelated-marker@example.com" });
        await otherContext.SaveChangesAsync();
        await first.CreateLeaseAsync();
        await first.DisposeAsync();
        await firstPool.DisposeAsync();

        Assert.Equal(otherIdentity, await ReadIdentityAsync(otherContext));
        Assert.Equal("unrelated-marker@example.com", (await otherContext.Users.SingleAsync()).Email);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Locked_rows_cause_a_bounded_reset_failure_and_are_never_reused()
    {
        var prefix = $"tt-locked-drain-{Guid.NewGuid():N}";
        using var docker = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
        await using var pool = new TtPool(new TtOptions { PoolSize = 1, ContainerPrefix = prefix });
        await pool.InitializeAsync();
        var database = await pool.GetDatabaseAsync();
        await using var connection = await database.DataSource.OpenConnectionAsync();
        await using (var create = new NpgsqlCommand("CREATE TABLE locked_reset (id integer)", connection))
            await create.ExecuteNonQueryAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var hold = new NpgsqlCommand("LOCK TABLE locked_reset IN ACCESS EXCLUSIVE MODE", connection, transaction))
            await hold.ExecuteNonQueryAsync();

        var error = await Assert.ThrowsAsync<TtLifecycleException>(
            () => database.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(20)));
        Assert.Equal("ResetRows", error.Stage);
        Assert.Equal("55P03", error.SqlState);
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => database.DataSource.OpenConnectionAsync().AsTask());
        await pool.DisposeAsync();
        Assert.Equal(0, pool.Snapshot.Reserved);
        Assert.Empty(await docker.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = true,
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["name"] = new Dictionary<string, bool> { [prefix] = true }
            }
        }));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Ordinary_rows_in_a_user_schema_starting_with_pg_are_removed()
    {
        await using var pool = new TtPool(new TtOptions { PoolSize = 1 });
        await pool.InitializeAsync();
        await using var factory = new TtFactory<PgNamedSchemaContext>(pool, new TtOptions());
        var contextLease = await factory.CreateLeaseAsync();
        var context = contextLease.Context;
        var identity = await ReadIdentityAsync(context);
        context.Users.Add(new User { Email = "non-public-marker@example.com" });
        await context.SaveChangesAsync();
        await contextLease.DisposeAsync();

        var nextLease = await factory.CreateLeaseAsync();
        var next = nextLease.Context;
        Assert.Equal(identity, await ReadIdentityAsync(next));
        Assert.Equal(0, await next.Users.CountAsync());
    }

    public sealed class PgNamedSchemaContext : DbContext
    {
        public PgNamedSchemaContext(DbContextOptions<PgNamedSchemaContext> options) : base(options) { }
        public DbSet<User> Users => Set<User>();
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<User>().ToTable("Users", "pgfixture");
    }
}
