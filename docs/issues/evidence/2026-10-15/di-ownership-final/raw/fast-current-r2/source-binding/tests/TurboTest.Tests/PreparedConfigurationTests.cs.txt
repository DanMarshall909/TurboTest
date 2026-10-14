using TurboTest.EFCore;
using TurboTest.EFCore.Internal;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace TurboTest.Tests;

public class PreparedConfigurationTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_configured_cached_image_is_prepared_and_used_by_the_fixture_host()
    {
        var alias = $"tt-image-proof-{Guid.NewGuid():N}";
        var prefix = $"tt-image-host-{Guid.NewGuid():N}";
        using var docker = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
        await docker.Images.TagImageAsync("postgres:16-alpine", new ImageTagParameters { RepositoryName = alias, Tag = "latest" });
        try
        {
            var definition = new TtBaselineDefinition<TestDbContext>("image-v1", TtSchemaInitialization.EnsureCreated);
            await using var fixture = await Tt.CreateFactoryAsync(definition, options =>
            {
                options.PoolSize = 1;
                options.DockerImage = alias + ":latest";
                options.ContainerPrefix = prefix;
            });
            await using var lease = await fixture.CreateLeaseAsync();
            Assert.True(await lease.Context.Database.CanConnectAsync());
            var owned = await docker.Containers.ListContainersAsync(new ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["name"] = new Dictionary<string, bool> { [prefix] = true }
                }
            });
            var host = Assert.Single(owned);
            var details = await docker.Containers.InspectContainerAsync(host.ID);
            Assert.Equal(alias + ":latest", details.Config.Image);
        }
        finally { await docker.Images.DeleteImageAsync(alias + ":latest", new ImageDeleteParameters { Force = false, NoPrune = true }); }
    }

    [Fact]
    public async Task A_null_baseline_is_rejected_before_configuration_runs()
    {
        var configured = 0;
        var error = await Record.ExceptionAsync(() => Tt.CreateFactoryAsync<TestDbContext>(
            (TtBaselineDefinition<TestDbContext>)null!, _ =>
            {
                configured++;
                throw new InvalidOperationException("Null baseline reached configuration.");
            }));
        Assert.Equal(0, configured);
        Assert.Equal("baseline", Assert.IsType<ArgumentNullException>(error).ParamName);
    }

    [Theory]
    [InlineData("PoolSize", 0)]
    [InlineData("PoolSize", -1)]
    [InlineData("MemoryLimitMb", 0)]
    [InlineData("MemoryLimitMb", -1)]
    [InlineData("Provider", 987)]
    [InlineData("AutoAllocateResources", 1)]
    [InlineData("EnableDynamicScaling", 1)]
    [InlineData("ConnectionStringTemplate", 1)]
    [InlineData("DockerImage", 0)]
    [InlineData("DockerImage", 1)]
    [InlineData("DatabaseName", 0)]
    [InlineData("DatabaseName", 1)]
    [InlineData("DatabaseName", 2)]
    [InlineData("DatabaseName", 3)]
    [InlineData("Username", 0)]
    [InlineData("Username", 1)]
    [InlineData("Username", 2)]
    [InlineData("Password", 0)]
    public async Task Invalid_or_unsupported_prepared_configuration_is_rejected_before_image_preparation(string property, int value)
    {
        var options = new TtOptions { PoolSize = 1 };
        switch (property)
        {
            case "PoolSize": options.PoolSize = value; break;
            case "MemoryLimitMb": options.MemoryLimitMb = value; break;
            case "Provider": options.Provider = (TtDatabaseProvider)value; break;
            case "AutoAllocateResources": options.AutoAllocateResources = true; break;
            case "EnableDynamicScaling": options.EnableDynamicScaling = true; break;
            case "ConnectionStringTemplate": options.ConnectionStringTemplate = "custom-template"; break;
            case "DockerImage": options.DockerImage = value == 0 ? "" : " "; break;
            case "DatabaseName": options.DatabaseName = value switch { 0 => "", 1 => " ", 2 => new string('x', 22), _ => new string('é', 11) }; break;
            case "Username": options.Username = value switch { 0 => " ", 1 => new string('x', 64), _ => new string('é', 32) }; break;
            case "Password": options.Password = ""; break;
        }
        var images = 0;
        var definition = new TtBaselineDefinition<TestDbContext>("options-v1", TtSchemaInitialization.EnsureCreated);
        var error = await Record.ExceptionAsync(() => Tt.CreatePreparedFactoryAsync(options, definition, default,
            prepareImage: _ =>
            {
                images++;
                throw new InvalidOperationException("Rejected configuration reached image preparation.");
            }));
        Assert.Equal(0, images);
        var argument = Assert.IsAssignableFrom<ArgumentException>(error);
        Assert.Equal(property, argument.ParamName);
    }

    [Fact]
    public async Task Image_preparation_receives_the_configured_image()
    {
        var requested = new List<string>();
        var stop = new IOException("Stop before Docker allocation.");
        await using var host = new TtPool(new TtOptions { PoolSize = 1, DockerImage = "fixture-image:chosen" },
            prepareImage: image =>
            {
                requested.Add(image);
                throw stop;
            });
        Assert.Same(stop, await Assert.ThrowsAsync<IOException>(() => host.InitializeAsync()));
        Assert.Equal(["fixture-image:chosen"], requested);
    }
}
