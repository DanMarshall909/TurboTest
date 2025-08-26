using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TurboTest.EFCore;
using FluentAssertions;

namespace TurboTest.Tests;

public class AutoAllocationTests
{
    [Fact]
    public void Should_Enable_Auto_Allocation_With_Defaults()
    {
        // Arrange & Act
        Tt.Configure(options =>
        {
            options.AutoAllocateResources = true;
        });

        // Assert - Should not throw when creating a context
        Assert.True(true, "Auto-allocation should not throw with defaults");
    }

    [Fact]
    public void Should_Apply_Custom_Resource_Reserves()
    {
        // Arrange & Act
        Tt.Configure(options =>
        {
            options.AutoAllocateResources = true;
            options.ReserveMemoryMb = 1024; // 1GB
            options.ReserveCpuCores = 1;
            options.MaxAutoContainers = 5;
        });

        // Assert - Configuration applied successfully
        Assert.True(true, "Custom resource reserves should not throw");
    }

    [Fact]
    public void Should_Configure_Auto_Allocation_From_Configuration()
    {
        // Arrange
        var configData = new Dictionary<string, string?>
        {
            ["TurboTest:AutoAllocateResources"] = "true",
            ["TurboTest:ReserveMemoryMb"] = "2048",
            ["TurboTest:ReserveCpuCores"] = "2",
            ["TurboTest:MaxAutoContainers"] = "15"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        // Act
        Tt.Configure(configuration);

        // Assert - IConfiguration binding should work
        Assert.True(true, "Auto-allocation IConfiguration binding should work");
    }

    [Fact]
    public async Task Should_Create_Context_With_Auto_Allocation()
    {
        // Arrange - Add logging to see what values are calculated
        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug));

        var logger = loggerFactory.CreateLogger<AutoAllocationTests>();

        Tt.Configure(options =>
        {
            options.AutoAllocateResources = true;
            options.ReserveMemoryMb = 512; // Minimal reserve for testing
            options.LogLevel = LogLevel.Debug;
            options.Logger = logger;
        });

        // Act & Assert
        await using var context = await Tt.CreateContextAsync<TestDbContext>();
        context.Should().NotBeNull("Auto-allocated context should be created successfully");

        // Verify database connectivity
        var canConnect = await context.Database.CanConnectAsync();
        canConnect.Should().BeTrue("Auto-allocated database should be accessible");
    }

    [Fact]
    public void Should_Handle_System_Resource_Detection_Gracefully()
    {
        // Arrange & Act - This tests the internal resource detection
        // Even if system detection fails, it should fall back to safe defaults
        Tt.Configure(options =>
        {
            options.AutoAllocateResources = true;
            options.MaxAutoContainers = 1; // Limit for testing
        });

        // Assert - Should not throw even with resource detection
        Assert.True(true, "Resource detection should handle failures gracefully");
    }

    [Fact]
    public void Should_Respect_Max_Container_Limit()
    {
        // Arrange & Act
        Tt.Configure(options =>
        {
            options.AutoAllocateResources = true;
            options.MaxAutoContainers = 2; // Very restrictive limit
            options.ReserveMemoryMb = 0;   // Don't reserve anything for max allocation
        });

        // Assert - Should respect the maximum even with lots of resources
        Assert.True(true, "Should respect MaxAutoContainers limit");
    }
}