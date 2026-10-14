using TurboTest.EFCore;
using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

public class ResourceBudgetValidationTests
{
    [Theory]
    [InlineData(false, nameof(TtOptions.DrainTimeout), 0)]
    [InlineData(true, nameof(TtOptions.DrainTimeout), 0)]
    [InlineData(false, nameof(TtOptions.DrainTimeout), -1)]
    [InlineData(true, nameof(TtOptions.DrainTimeout), -1)]
    [InlineData(false, nameof(TtOptions.DrainTimeout), 4294967295)]
    [InlineData(true, nameof(TtOptions.DrainTimeout), 4294967295)]
    [InlineData(false, nameof(TtOptions.StartupTimeout), 0)]
    [InlineData(true, nameof(TtOptions.StartupTimeout), 0)]
    [InlineData(false, nameof(TtOptions.StartupTimeout), -1)]
    [InlineData(true, nameof(TtOptions.StartupTimeout), -1)]
    [InlineData(false, nameof(TtOptions.StartupTimeout), 4294967295)]
    [InlineData(true, nameof(TtOptions.StartupTimeout), 4294967295)]
    public async Task A_lifecycle_deadline_must_be_positive_finite_and_supported_before_preparation(bool prepared, string field, long milliseconds)
    {
        var options = new TtOptions { PoolSize = 1 };
        typeof(TtOptions).GetProperty(field)!.SetValue(options, TimeSpan.FromMilliseconds(milliseconds));
        IAsyncDisposable? pool = null;
        try
        {
            var error = Assert.Throws<ArgumentOutOfRangeException>(() => pool = prepared
                ? new TtPreparedPool<TestDbContext>(options,
                    new TtBaselineDefinition<TestDbContext>("deadline-v1", TtSchemaInitialization.EnsureCreated))
                : new TtPool(options));
            Assert.Equal(field, error.ParamName);
        }
        finally { if (pool is not null) await pool.DisposeAsync(); }
    }

    [Theory]
    [InlineData(false, nameof(TtOptions.PoolSize))]
    [InlineData(true, nameof(TtOptions.PoolSize))]
    [InlineData(false, nameof(TtOptions.PreparationConcurrency))]
    [InlineData(true, nameof(TtOptions.PreparationConcurrency))]
    [InlineData(false, nameof(TtOptions.ResetConcurrency))]
    [InlineData(true, nameof(TtOptions.ResetConcurrency))]
    [InlineData(false, nameof(TtOptions.MaxConnectionsPerDatabase))]
    [InlineData(true, nameof(TtOptions.MaxConnectionsPerDatabase))]
    [InlineData(false, nameof(TtOptions.MaxAdministratorConnections))]
    [InlineData(true, nameof(TtOptions.MaxAdministratorConnections))]
    [InlineData(false, nameof(TtOptions.MaxContainers))]
    [InlineData(true, nameof(TtOptions.MaxContainers))]
    public async Task A_nonpositive_resource_budget_is_rejected_before_preparation(bool prepared, string field)
    {
        var options = new TtOptions { PoolSize = 1 };
        typeof(TtOptions).GetProperty(field)!.SetValue(options, 0);
        IAsyncDisposable? pool = null;
        try
        {
            var error = Assert.Throws<ArgumentOutOfRangeException>(() => pool = prepared
                ? new TtPreparedPool<TestDbContext>(options,
                    new TtBaselineDefinition<TestDbContext>("limits-v1", TtSchemaInitialization.EnsureCreated))
                : new TtPool(options));
            Assert.Equal(field, error.ParamName);
        }
        finally { if (pool is not null) await pool.DisposeAsync(); }
    }

    [Fact]
    public async Task An_ordinary_fixture_cannot_warm_more_slots_than_its_container_ceiling()
    {
        TtPool? pool = null;
        try
        {
            var error = Assert.Throws<ArgumentException>(() =>
                pool = new TtPool(new TtOptions { PoolSize = 2, MaxContainers = 1 }));
            Assert.Equal(nameof(TtOptions.MaxContainers), error.ParamName);
        }
        finally { if (pool is not null) await pool.DisposeAsync(); }
    }
}
