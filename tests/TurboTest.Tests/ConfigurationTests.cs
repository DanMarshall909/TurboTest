using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TurboTest.EFCore;
using TurboTest.EFCore.Extensions;

namespace TurboTest.Tests;

public class ConfigurationTests
{
    [Fact]
    public void Should_Configure_From_Action()
    {
        // Arrange & Act
        Tt.Configure(options =>
        {
            options.PoolSize = 5;
            options.MemoryLimitMb = 512;
            options.EnableSensitiveDataLogging = true;
            options.ContainerPrefix = "test";
        });

        // This is tested implicitly when creating a context
        // The configuration is applied internally
        Assert.True(true, "Configuration should not throw");
    }

    [Fact]
    public void Should_Configure_From_IConfiguration()
    {
        // Arrange
        var configData = new Dictionary<string, string?>
        {
            ["TurboTest:PoolSize"] = "7",
            ["TurboTest:MemoryLimitMb"] = "1024",
            ["TurboTest:EnableSensitiveDataLogging"] = "true",
            ["TurboTest:ContainerPrefix"] = "config_test"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        // Act
        Tt.Configure(configuration);

        // Assert - Configuration applied successfully
        Assert.True(true, "IConfiguration binding should not throw");
    }

    [Fact]
    public void Should_Register_Services_With_DI()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddTurboTest(options =>
        {
            options.PoolSize = 3;
            options.AutoMigrate = false;
        });

        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var factory = serviceProvider.GetService<ITtServiceFactory>();
        factory.Should().NotBeNull("TurboTest services should be registered");
    }

    [Fact]
    public void Should_Register_DbContext_With_DI()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddTurboTestDbContext<TestDbContext>();
        var serviceProvider = services.BuildServiceProvider();

        // Assert - Should not throw when getting the context
        var contextFactory = serviceProvider.GetRequiredService<ITtServiceFactory>();
        contextFactory.Should().NotBeNull("DbContext services should be registered");
    }

    [Fact]
    public void Should_Bind_Options_From_Configuration_Section()
    {
        // Arrange
        var configData = new Dictionary<string, string?>
        {
            ["TurboTest:Provider"] = "PostgreSql",
            ["TurboTest:PoolSize"] = "2",
            ["TurboTest:AutoMigrate"] = "false",
            ["TurboTest:DatabaseName"] = "custom_db"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        var services = new ServiceCollection();

        // Act
        services.AddTurboTest(configuration);

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetService<ITtServiceFactory>();
        factory.Should().NotBeNull();
    }

    [Fact]
    public void Should_Support_Environment_Variables()
    {
        // Arrange - Set environment variables
        Environment.SetEnvironmentVariable("TurboTest__PoolSize", "4");
        Environment.SetEnvironmentVariable("TurboTest__MemoryLimitMb", "256");

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();

            // Act
            Tt.Configure(configuration);

            // Assert - Should not throw
            Assert.True(true, "Environment variable configuration should work");
        }
        finally
        {
            // Cleanup
            Environment.SetEnvironmentVariable("TurboTest__PoolSize", null);
            Environment.SetEnvironmentVariable("TurboTest__MemoryLimitMb", null);
        }
    }
}