using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.EntityFrameworkCore;
using TurboTest.EFCore;

namespace TurboTest.Tests;

[Collection("Static lifecycle")]
public class StaticInitializationTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Concurrent_static_initialization_and_disposal_drain_one_owner_before_reconfiguration_and_reuse()
    {
        await Tt.DisposeAsync();
        var prefix = $"tt-static-init-{Guid.NewGuid():N}";
        Tt.Configure(options =>
        {
            options.PoolSize = 1;
            options.AutoAllocateResources = false;
            options.ContainerPrefix = prefix;
        });
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var starts = 0;
        var initializing = Tt.CreateLeaseAsync<TestDbContext>(null, default, null, async _ =>
        {
            Assert.Equal(1, Interlocked.Increment(ref starts));
            entered.TrySetResult();
            await release.Task;
        });
        using var docker = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
        Task? closing = null;
        TtLease<TestDbContext>? published = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var original = Assert.Single(await OwnedAsync()).ID;
            var policy = Assert.Throws<InvalidOperationException>(() => Tt.Configure(options => options.PoolSize = 2));
            Assert.Contains("DisposeAsync", policy.Message);
            closing = Tt.DisposeAsync();
            Assert.False(closing.IsCompleted);
            Assert.Equal(original, Assert.Single(await OwnedAsync()).ID);
            release.TrySetResult();
            var creationFailure = await Record.ExceptionAsync(async () => published = await initializing);
            Assert.True(creationFailure is null or ObjectDisposedException, creationFailure?.ToString());
            await closing.WaitAsync(TimeSpan.FromSeconds(15));
            if (published is not null) Assert.Throws<ObjectDisposedException>(() => _ = published.Context);
            Assert.Equal(1, starts);
            Assert.Empty(await OwnedAsync());
            Tt.Configure(options => options.DatabaseName = "static_recreated");
            await using var recreated = await Tt.CreateLeaseAsync<TestDbContext>();
            Assert.StartsWith("static_recreated_", (await PooledResetTests.ReadIdentityAsync(recreated.Context)).Database);
            Assert.Equal(0, await recreated.Context.Users.CountAsync());
            Assert.NotEqual(original, Assert.Single(await OwnedAsync()).ID);
        }
        finally
        {
            release.TrySetResult();
            await Record.ExceptionAsync(() => initializing);
            if (closing is not null) await Record.ExceptionAsync(() => closing);
            if (published is not null) await published.DisposeAsync();
            await Tt.DisposeAsync();
        }

        Task<IList<ContainerListResponse>> OwnedAsync() => docker.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = true,
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["name"] = new Dictionary<string, bool> { [prefix] = true }
            }
        });
    }
}
