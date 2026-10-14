using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Transactions;
using TurboTest.EFCore;
using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

public class ConnectionBudgetTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "PostgreSQL")]
    public async Task Owned_contexts_wait_at_the_database_connection_cap_and_reuse_a_released_session(bool prepared)
    {
        void Configure(TtOptions options)
        {
            options.PoolSize = 1;
            options.MaxConnectionsPerDatabase = 2;
        }
        await using var fixture = prepared
            ? await Tt.CreateFactoryAsync(new TtBaselineDefinition<TestDbContext>("connections-v1",
                TtSchemaInitialization.EnsureCreated), Configure)
            : await Tt.CreateFactoryAsync<TestDbContext>(Configure);
        await using var lease = await fixture.CreateLeaseAsync();
        var second = lease.CreateContext();
        var third = lease.CreateContext();
        await lease.Context.Database.OpenConnectionAsync();
        await second.Database.OpenConnectionAsync();
        var firstSession = await SessionAsync(lease.Context);
        Assert.NotEqual(firstSession, await SessionAsync(second));
        Assert.Equal(2, fixture.ResourceBudget.MaxConnectionsPerDatabase);
        Assert.Equal(prepared ? 4L : 2L, fixture.ResourceBudget.MaximumManagedConnections);
        Assert.Equal(prepared ? 3L : 1L, fixture.ResourceBudget.MaximumOwnedDatabases);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => third.Database.OpenConnectionAsync(
            cancellation.Token).WaitAsync(TimeSpan.FromSeconds(5)));
        await lease.Context.Database.CloseConnectionAsync();
        using var nextCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await third.Database.OpenConnectionAsync(nextCancellation.Token);
        Assert.Equal(firstSession, await SessionAsync(third));
    }

    private static async Task<int> SessionAsync(DbContext context)
    {
        await using var command = new NpgsqlCommand("SELECT pg_backend_pid()",
            (NpgsqlConnection)context.Database.GetDbConnection());
        return (int)(await command.ExecuteScalarAsync())!;
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Administrator_allowance_includes_idle_control_and_executing_reset_connections()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        await using var fixture = await Tt.CreatePreparedFactoryAsync(new TtOptions
        {
            PoolSize = 3, ResetConcurrency = 2, MaxAdministratorConnections = 1
        }, new TtBaselineDefinition<TestDbContext>("administrators-v1", TtSchemaInitialization.EnsureCreated), default,
            afterSequenceRestore: async (_, _) =>
            {
                if (Interlocked.Increment(ref active) > 1)
                    throw new InvalidOperationException("Two administrator resets exceeded the allowance.");
                entered.TrySetResult();
                try { await release.Task; }
                finally { Interlocked.Decrement(ref active); }
            });
        var first = await fixture.CreateLeaseAsync();
        var second = await fixture.CreateLeaseAsync();
        await using var observer = await fixture.CreateLeaseAsync();
        Task? firstReturn = null;
        Task? secondReturn = null;
        try
        {
            firstReturn = first.DisposeAsync().AsTask();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal((1, 1, 13L), (fixture.ResourceBudget.ContainerLimit,
                fixture.ResourceBudget.AdministratorConnectionAllowance, fixture.ResourceBudget.MaximumManagedConnections));
            Assert.Equal(1, await AdministratorCountAsync(observer.Context));
            secondReturn = second.DisposeAsync().AsTask();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (fixture.PoolState.WaitingAdministratorConnections != 1)
                await Task.Delay(5, deadline.Token);
            Assert.Equal((2, 1, 1), (fixture.PoolState.ExecutingResets,
                fixture.PoolState.ExecutingAdministratorConnections, fixture.PoolState.WaitingAdministratorConnections));
            Assert.False(secondReturn.IsCompleted);
            Assert.Equal(1, await AdministratorCountAsync(observer.Context));
            release.TrySetResult();
            await Task.WhenAll(firstReturn, secondReturn);
            Assert.Equal((0, 0), (fixture.PoolState.ExecutingAdministratorConnections,
                fixture.PoolState.WaitingAdministratorConnections));
            Assert.Equal(0, await AdministratorCountAsync(observer.Context));
        }
        finally
        {
            release.TrySetResult();
            if (firstReturn is not null) await firstReturn;
            if (secondReturn is not null) await secondReturn;
            await first.DisposeAsync();
            await second.DisposeAsync();
        }
    }

    private static Task<int> AdministratorCountAsync(DbContext context) => context.Database.SqlQueryRaw<int>(
        "SELECT count(*)::integer AS \"Value\" FROM pg_stat_activity WHERE usename LIKE 'tt_admin_%' AND datname IS NOT NULL")
        .SingleAsync();

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Returning_a_prepared_lease_inside_an_ambient_transaction_restores_it_without_retaining_private_sessions()
    {
        await using var fixture = await Tt.CreateFactoryAsync(new TtBaselineDefinition<TestDbContext>(
            "ambient-return-v1", TtSchemaInitialization.EnsureCreated), options =>
            {
                options.PoolSize = 2;
                options.MaxAdministratorConnections = 1;
            });
        var lease = await fixture.CreateLeaseAsync();
        await using var observer = await fixture.CreateLeaseAsync();
        lease.Context.Users.Add(new User { Email = "dirty-before-return@example.test" });
        await lease.Context.SaveChangesAsync();
        using (var transaction = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            await lease.DisposeAsync();
            Assert.Equal(1, fixture.PoolState.Ready);
            Assert.Equal(0, fixture.PoolState.ExecutingAdministratorConnections);
            Assert.Equal(0, await AdministratorCountAsync(observer.Context));
            transaction.Complete();
        }
        await using var next = await fixture.CreateLeaseAsync();
        Assert.Empty(await next.Context.Users.ToListAsync());
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Failed_and_canceled_administrator_opens_release_only_their_own_permits()
    {
        var password = Guid.NewGuid().ToString("N");
        await using var fixture = await Tt.CreateFactoryAsync<TestDbContext>(options =>
        {
            options.PoolSize = 1;
            options.Password = password;
        });
        await using var database = await fixture.CreateLeaseAsync();
        var connectionString = new NpgsqlConnectionStringBuilder(database.Context.Database.GetDbConnection().ConnectionString)
        {
            Pooling = false, Enlist = false, Password = password
        };
        var failConstruction = true;
        var failAuthentication = true;
        var attempts = 0;
        var administrators = new TtAdministratorConnections(1, _ =>
        {
            Interlocked.Increment(ref attempts);
            if (failConstruction) throw new InvalidOperationException("Connection construction failed.");
            var builder = new NpgsqlConnectionStringBuilder(connectionString.ConnectionString);
            if (failAuthentication) builder.Password = "deliberately-invalid";
            return new NpgsqlConnection(builder.ConnectionString);
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => administrators.OpenAsync());
        Assert.Equal((0, 0), administrators.Snapshot);
        failConstruction = false;
        var rejected = await Assert.ThrowsAsync<PostgresException>(() => administrators.OpenAsync());
        Assert.Equal("28P01", rejected.SqlState);
        Assert.Equal((0, 0), administrators.Snapshot);
        failAuthentication = false;
        await using var first = await administrators.OpenAsync();
        Assert.Equal(System.Data.ConnectionState.Open, first.Connection.State);
        using var canceled = new CancellationTokenSource();
        var waiting = administrators.OpenAsync(cancellationToken: canceled.Token);
        Assert.Equal((1, 1), administrators.Snapshot);
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.Equal((1, 0), administrators.Snapshot);
        Assert.Equal(3, attempts);
        using var nextDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var next = administrators.OpenAsync(cancellationToken: nextDeadline.Token);
        Assert.False(next.IsCompleted);
        await first.DisposeAsync();
        await first.DisposeAsync();
        await using var second = await next;
        await using var query = new NpgsqlCommand("SELECT 1", second.Connection);
        Assert.Equal(1, await query.ExecuteScalarAsync());
        Assert.Equal((1, 0), administrators.Snapshot);
        await second.DisposeAsync();
        Assert.Equal((0, 0), administrators.Snapshot);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task User_workload_changes_still_roll_back_with_the_callers_ambient_transaction()
    {
        await using var fixture = await Tt.CreateFactoryAsync(new TtBaselineDefinition<TestDbContext>(
            "ambient-workload-v1", TtSchemaInitialization.EnsureCreated), options => options.PoolSize = 1);
        await using var lease = await fixture.CreateLeaseAsync();
        using (var transaction = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            lease.Context.Users.Add(new User { Email = "rolled-back@example.test" });
            await lease.Context.SaveChangesAsync();
            Assert.Single(await lease.Context.Users.ToListAsync());
        }
        Assert.Empty(await lease.Context.Users.AsNoTracking().ToListAsync());
    }
}
