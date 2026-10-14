using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TurboTest.EFCore;
using TurboTest.EFCore.Extensions;

namespace TurboTest.Tests;

public class ConfigurationTests
{
    [Fact]
    public async Task Service_registration_resolves_an_async_disposable_factory()
    {
        var services = new ServiceCollection();
        services.AddTurboTest(o => o.PoolSize = 1);
        await using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<ITtServiceFactory>());
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Configuration_section_is_consumed_by_the_public_fixture()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TurboTest:PoolSize"] = "1",
            ["TurboTest:DatabaseName"] = "configured_database",
            ["TurboTest:EnableSensitiveDataLogging"] = "false"
        }).Build();
        await using var fixture = await Tt.CreateFactoryAsync<TestDbContext>(o =>
            configuration.GetSection(TtOptions.SectionName).Bind(o));
        await using var lease = await fixture.CreateLeaseAsync();
        var identity = await PooledResetTests.ReadIdentityAsync(lease.Context);
        Assert.StartsWith("configured_database_", identity.Database);
        Assert.True(await lease.Context.Database.CanConnectAsync());
    }
}
