using Microsoft.EntityFrameworkCore;
using Npgsql;
using TurboTest.Benchmarks;
using TurboTest.EFCore;

namespace TurboTest.Tests;

public class BenchmarkNativeWorkloadTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Actual_benchmark_rows_seeds_sequences_and_isolation_are_verified_before_timing()
    {
        await using var fixture = await Tt.CreateFactoryAsync(BenchmarkWorkload.Baseline, options => options.PoolSize = 2);
        var one = await fixture.CreateLeaseAsync(); var two = await fixture.CreateLeaseAsync();
        var firstIdentity = await Identity(one.Context); var secondIdentity = await Identity(two.Context);
        Assert.NotEqual(firstIdentity, secondIdentity);
        var identities = new[] { firstIdentity, secondIdentity };
        await one.DisposeAsync(); await two.DisposeAsync();
        foreach (var (total, parents, children, notes) in new[] { (130, 100, 20, 10), (1300, 1000, 200, 100), (13000, 10000, 2000, 1000) })
        {
            await using var lease = await fixture.CreateLeaseAsync();
            await using var other = await fixture.CreateLeaseAsync();
            Assert.Contains(await Identity(lease.Context), identities);
            Assert.Contains(await Identity(other.Context), identities);
            await BenchmarkWorkload.VerifyBaselineAsync(lease.Context);
            var marker = "load-" + Guid.NewGuid().ToString("N");
            await BenchmarkWorkload.LoadAsync(lease.Context, WorkloadMix.ForTotal(total), marker);
            await BenchmarkWorkload.ValidateMutatedAsync(lease.Context, WorkloadMix.ForTotal(total), marker);
            var notBaseline = await Assert.ThrowsAsync<InvalidDataException>(() => BenchmarkWorkload.VerifyBaselineAsync(lease.Context));
            Assert.Contains("BaselineMismatch", notBaseline.Message);
            Assert.Equal(parents + 1, await lease.Context.Parents.CountAsync());
            Assert.Equal(children, await lease.Context.Children.CountAsync());
            Assert.Equal(notes, await lease.Context.Notes.CountAsync());
            var seed = await lease.Context.Parents.SingleAsync(row => row.Id == 100);
            Assert.Equal(("changed-seed", 91), (seed.Name, seed.Priority));
            Assert.False(await lease.Context.Children.AnyAsync(row => row.Id == 200));
            Assert.False(await lease.Context.Notes.AnyAsync(row => row.Id == 300));
            Assert.Equal(101, await lease.Context.Parents.Where(row => row.Id != 100).MinAsync(row => row.Id));
            Assert.Equal(201, await lease.Context.Children.MinAsync(row => row.Id));
            Assert.Equal(301, await lease.Context.Notes.MinAsync(row => row.Id));
            Assert.False(await other.Context.Parents.AnyAsync(row => row.Name.StartsWith(marker)));
            Assert.Equal((1, 1, 1), (await other.Context.Parents.CountAsync(), await other.Context.Children.CountAsync(), await other.Context.Notes.CountAsync()));
            if (total == 130)
            {
                await lease.Context.Parents.Where(row => row.Id == 100).ExecuteUpdateAsync(update => update.SetProperty(row => row.Priority, 17));
                var wrongSeed = await Assert.ThrowsAsync<InvalidDataException>(() => BenchmarkWorkload.ValidateMutatedAsync(lease.Context, WorkloadMix.ForTotal(total), marker));
                Assert.Contains("SeedMutation", wrongSeed.Message);
                await lease.Context.Parents.Where(row => row.Id == 100).ExecuteUpdateAsync(update => update.SetProperty(row => row.Priority, 91));
                await lease.Context.Database.ExecuteSqlRawAsync("INSERT INTO \"Parents\"(\"Name\",\"Priority\") VALUES ('foreign-operation',43)");
                var foreign = await Assert.ThrowsAsync<InvalidDataException>(() => BenchmarkWorkload.ValidateMutatedAsync(lease.Context, WorkloadMix.ForTotal(total), marker));
                Assert.Contains("ForeignMarker", foreign.Message);
                await lease.Context.Parents.Where(row => row.Name == "foreign-operation").ExecuteDeleteAsync();
                await lease.Context.Notes.ExecuteDeleteAsync();
                var wrongMix = await Assert.ThrowsAsync<InvalidDataException>(() => BenchmarkWorkload.ValidateMutatedAsync(lease.Context, WorkloadMix.ForTotal(total), marker));
                Assert.Contains("WorkloadDistribution", wrongMix.Message);
                await lease.Context.Children.ExecuteDeleteAsync();
                await lease.Context.Parents.Where(row => row.Id != 100).ExecuteDeleteAsync();
                var empty = await Assert.ThrowsAsync<InvalidDataException>(() => BenchmarkWorkload.ValidateMutatedAsync(lease.Context, WorkloadMix.ForTotal(total), marker));
                Assert.Contains("WorkloadEmpty", empty.Message);
            }
        }
        await using (var wrongSequence = await fixture.CreateLeaseAsync())
        {
            await wrongSequence.Context.Database.ExecuteSqlRawAsync("SELECT setval(pg_get_serial_sequence('\"Parents\"', 'Id'),399,true)");
            var marker = "sequence-" + Guid.NewGuid().ToString("N");
            await BenchmarkWorkload.LoadAsync(wrongSequence.Context, WorkloadMix.ForTotal(130), marker);
            var error = await Assert.ThrowsAsync<InvalidDataException>(() => BenchmarkWorkload.ValidateMutatedAsync(wrongSequence.Context, WorkloadMix.ForTotal(130), marker));
            Assert.Contains("Sequence", error.Message);
        }
        await using (var final = await fixture.CreateLeaseAsync())
        {
            await BenchmarkWorkload.VerifyBaselineAsync(final.Context);
            var parent = await final.Context.Parents.SingleAsync(); var child = await final.Context.Children.SingleAsync(); var note = await final.Context.Notes.SingleAsync();
            Assert.Equal((100, "baseline-parent", 17), (parent.Id, parent.Name, parent.Priority));
            Assert.Equal((200, 100, "baseline-child", true), (child.Id, child.ParentId, child.Name, child.Enabled));
            Assert.Equal((300, 200, "baseline-note", 29), (note.Id, note.ChildId, note.Body, note.Revision));
        }
        await fixture.DisposeAsync();
        Assert.Equal(0, fixture.PoolState.Reserved);
    }

    private static async Task<(string Name, uint Oid)> Identity(BenchmarkContext context)
    {
        await context.Database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("SELECT current_database(),oid FROM pg_database WHERE datname=current_database()", (NpgsqlConnection)context.Database.GetDbConnection());
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.GetFieldValue<uint>(1));
    }
}
