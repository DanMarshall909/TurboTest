using TurboTest.EFCore;
using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

public class OrdinaryPoolClosureTests
{
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
