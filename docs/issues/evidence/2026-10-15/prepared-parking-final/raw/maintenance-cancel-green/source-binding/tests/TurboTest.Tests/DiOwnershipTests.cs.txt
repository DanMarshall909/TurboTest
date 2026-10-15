using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TurboTest.EFCore;
using TurboTest.EFCore.Extensions;

namespace TurboTest.Tests;

[Collection("Static lifecycle")]
public class DiOwnershipTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Independent_providers_prepare_their_own_registered_baselines_and_preserve_options()
    {
        var firstPrefix = $"tt-di-first-{Guid.NewGuid():N}";
        var otherPrefix = $"tt-di-other-{Guid.NewGuid():N}";
        var logger = new RecordingLogger();
        ServiceProvider Provider(string prefix, string email)
        {
            var services = new ServiceCollection();
            services.AddTurboTest(options =>
            {
                options.PoolSize = 1;
                options.MaxConnectionsPerDatabase = 2;
                options.MaxAdministratorConnections = 1;
                options.ContainerPrefix = prefix;
                options.DatabaseName = "di_baseline";
                options.DockerImage = "postgres:16-alpine";
                options.DockerHost = "unix:///var/run/docker.sock";
                options.MemoryLimitMb = 192;
                options.Logger = logger;
                options.LogLevel = LogLevel.Debug;
                options.EnableSensitiveDataLogging = false;
            });
            services.AddSingleton(new TtBaselineDefinition<TestDbContext>("same-di-version",
                TtSchemaInitialization.EnsureCreated, async (context, token) =>
                {
                    context.Users.Add(new User { Email = email });
                    await context.SaveChangesAsync(token);
                }));
            services.AddTurboTestDbContext<TestDbContext>();
            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        }
        await using var first = Provider(firstPrefix, "first-di@example.test");
        await using var other = Provider(otherPrefix, "other-di@example.test");
        using var docker = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
        await using var a = first.CreateAsyncScope();
        await using var b = other.CreateAsyncScope();
        await a.ServiceProvider.InitializeTurboTestAsync<TestDbContext>();
        await b.ServiceProvider.InitializeTurboTestAsync<TestDbContext>();
        var firstContext = a.ServiceProvider.GetRequiredService<TtLease<TestDbContext>>().Context;
        var otherContext = b.ServiceProvider.GetRequiredService<TtLease<TestDbContext>>().Context;
        Assert.Equal("first-di@example.test", (await firstContext.Users.SingleAsync()).Email);
        Assert.Equal("other-di@example.test", (await otherContext.Users.SingleAsync()).Email);
        Assert.NotEqual((await PooledResetTests.ReadIdentityAsync(firstContext)).Database,
            (await PooledResetTests.ReadIdentityAsync(otherContext)).Database);
        var owner = await first.GetRequiredService<ITtServiceFactory>().CreateFactoryAsync<TestDbContext>();
        Assert.Equal((1, 1, 2, 1), (owner.ResourceBudget.WorkloadSlotLimit, owner.ResourceBudget.ContainerLimit,
            owner.ResourceBudget.MaxConnectionsPerDatabase, owner.ResourceBudget.AdministratorConnectionAllowance));
        Assert.Equal(1, owner.PoolState.Leased);
        var firstHost = Assert.Single(await OwnedAsync(firstPrefix)).ID;
        var configuredHost = await docker.Containers.InspectContainerAsync(firstHost);
        Assert.Equal("postgres:16-alpine", configuredHost.Config.Image);
        Assert.Equal(192L * 1024 * 1024, configuredHost.HostConfig.Memory);
        await a.DisposeAsync();
        Assert.Contains(400, logger.Events);
        await first.DisposeAsync();
        Assert.Empty(await OwnedAsync(firstPrefix));
        Assert.Single(await OwnedAsync(otherPrefix));
        Assert.Equal("other-di@example.test", (await otherContext.Users.SingleAsync()).Email);

        Task<IList<ContainerListResponse>> OwnedAsync(string prefix) => docker.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = true,
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["name"] = new Dictionary<string, bool> { [prefix] = true }
            }
        });
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Initialized_async_scopes_resolve_query_and_return_their_configured_context()
    {
        var services = new ServiceCollection();
        services.AddTurboTest(options => { options.PoolSize = 1; options.DatabaseName = "di_scoped"; });
        services.AddTurboTestDbContext<TestDbContext>();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        (string Database, uint Oid, int Session) firstIdentity;
        await using (var first = provider.CreateAsyncScope())
        {
            await first.ServiceProvider.InitializeTurboTestAsync<TestDbContext>();
            var context = first.ServiceProvider.GetRequiredService<TtLease<TestDbContext>>().Context;
            Assert.Same(context, first.ServiceProvider.GetRequiredService<TtLease<TestDbContext>>().Context);
            firstIdentity = await PooledResetTests.ReadIdentityAsync(context);
            Assert.StartsWith("di_scoped_", firstIdentity.Database);
            context.Users.Add(new User { Email = "scope@example.test" });
            await context.SaveChangesAsync();
            Assert.Equal(1, await context.Users.CountAsync());
        }
        await using (var second = provider.CreateAsyncScope())
        {
            await second.ServiceProvider.InitializeTurboTestAsync<TestDbContext>();
            var context = second.ServiceProvider.GetRequiredService<TtLease<TestDbContext>>().Context;
            Assert.Equal(firstIdentity, await PooledResetTests.ReadIdentityAsync(context));
            Assert.Empty(await context.Users.ToListAsync());
        }
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Service_leases_and_factories_share_the_configured_provider_owner()
    {
        await Tt.DisposeAsync();
        var prefix = $"tt-di-owner-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddTurboTest(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TurboTest:PoolSize"] = "1",
            ["TurboTest:MaxConnectionsPerDatabase"] = "2",
            ["TurboTest:ContainerPrefix"] = prefix,
            ["TurboTest:DatabaseName"] = "di_owned",
            ["TurboTest:DockerHost"] = "unix:///var/run/docker.sock"
        }).Build());
        services.AddTurboTest(options => options.EnableSensitiveDataLogging = false);
        await using var provider = services.BuildServiceProvider();
        using var docker = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
        try
        {
            var service = provider.GetRequiredService<ITtServiceFactory>();
            await using var first = await service.CreateLeaseAsync<TestDbContext>();
            var identity = await PooledResetTests.ReadIdentityAsync(first.Context);
            Assert.StartsWith("di_owned_", identity.Database);
            Assert.Equal(0, await first.Context.Users.CountAsync());
            await first.DisposeAsync();
            var factory = await service.CreateFactoryAsync<TestDbContext>();
            Assert.Same(factory, await service.CreateFactoryAsync<TestDbContext>());
            Assert.Equal(2, factory.ResourceBudget.MaxConnectionsPerDatabase);
            await using var second = await factory.CreateLeaseAsync();
            Assert.Equal(identity, await PooledResetTests.ReadIdentityAsync(second.Context));
            Assert.Single(await docker.Containers.ListContainersAsync(new ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["name"] = new Dictionary<string, bool> { [prefix] = true }
                }
            }));
        }
        finally { await Tt.DisposeAsync(); }
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_configured_unavailable_Docker_host_cannot_fall_back_to_global_resources()
    {
        var prefix = $"tt-di-unavailable-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddTurboTest(options =>
        {
            options.PoolSize = 1;
            options.ContainerPrefix = prefix;
            options.DockerHost = $"unix:///tmp/{prefix}.sock";
        });
        await using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<ITtServiceFactory>();
        var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => service.CreateFactoryAsync<TestDbContext>());
        Assert.Equal("ContainerInitialize", failure.Stage);
        using var docker = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
        Assert.Empty(await docker.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = true,
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["name"] = new Dictionary<string, bool> { [prefix] = true }
            }
        }));
    }

    private sealed class RecordingLogger : ILogger
    {
        internal System.Collections.Concurrent.ConcurrentQueue<int> Events { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Events.Enqueue(eventId.Id);
    }
}
