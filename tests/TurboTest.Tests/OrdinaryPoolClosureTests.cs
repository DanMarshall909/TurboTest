using TurboTest.EFCore;
using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

public class OrdinaryPoolClosureTests
{
    [Fact]
    public async Task Failed_image_preparation_releases_initialization_admission_for_a_second_attempt()
    {
        var attempts = 0;
        await using var pool = new TtPool(new TtOptions { PoolSize = 1 }, prepareImage: _ =>
        {
            var attempt = Interlocked.Increment(ref attempts);
            throw new IOException($"image preparation attempt {attempt}");
        });

        var first = await Assert.ThrowsAsync<IOException>(() => pool.InitializeAsync());
        var second = await Assert.ThrowsAsync<IOException>(() => pool.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal("image preparation attempt 1", first.Message);
        Assert.Equal("image preparation attempt 2", second.Message);
        Assert.Equal(2, attempts);
        Assert.Equal(0, pool.Snapshot.Reserved);
        Assert.Empty(pool.OwnedResourceIds);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Image_preparation_failure_allows_the_same_pool_to_acquire_and_release_its_configured_capacity()
    {
        var attempts = 0;
        await using var pool = new TtPool(new TtOptions
        {
            PoolSize = 1,
            ContainerPrefix = "tt_init_retry_" + Guid.NewGuid().ToString("N")
        }, prepareImage: _ =>
        {
            if (Interlocked.Increment(ref attempts) == 1) throw new IOException("First image preparation failed.");
            return Task.CompletedTask;
        });

        await Assert.ThrowsAsync<IOException>(() => pool.InitializeAsync());
        Assert.Equal(0, pool.Snapshot.Reserved);
        Assert.Empty(pool.OwnedResourceIds);
        await pool.InitializeAsync();
        var original = Assert.Single(pool.OwnedResourceIds);
        Assert.Equal(1, pool.Snapshot.Reserved);
        await using (var database = await pool.GetDatabaseAsync())
            Assert.NotNull(database.DataSource);
        Assert.Equal(original, Assert.Single(pool.OwnedResourceIds));
        await pool.DisposeAsync();
        Assert.Empty(pool.OwnedResourceIds);
        Assert.Equal(0, pool.Snapshot.Reserved);
        Assert.Equal(2, attempts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Closed_pool_rejects_initialization_before_repeating_image_preparation(bool failedInitialization)
    {
        var imagePreparations = 0;
        await using var pool = new TtPool(new TtOptions { PoolSize = 1 }, prepareImage: _ =>
        {
            Interlocked.Increment(ref imagePreparations);
            throw new IOException("private-image-preparation");
        });
        if (failedInitialization)
            await Assert.ThrowsAsync<IOException>(() => pool.InitializeAsync());

        await pool.DisposeAsync();
        await pool.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => pool.InitializeAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => pool.GetDatabaseAsync());
        Assert.Equal(failedInitialization ? 1 : 0, imagePreparations);
        Assert.Empty(pool.OwnedResourceIds);
        Assert.Equal(0, pool.Snapshot.Reserved);
    }
}
