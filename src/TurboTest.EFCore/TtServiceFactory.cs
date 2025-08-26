using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TurboTest.EFCore.Internal;

namespace TurboTest.EFCore;

/// <summary>
/// Service factory implementation for DI scenarios
/// </summary>
internal class TtServiceFactory : ITtServiceFactory, IAsyncDisposable
{
    private readonly TtOptions _options;
    private readonly TtPool _pool;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public TtServiceFactory(IOptions<TtOptions> options)
    {
        _options = options.Value;
        _pool = new TtPool(_options);
    }

    public string GetConnectionString()
    {
        // For DI scenarios, we might want to return a stable connection string
        // This is a simplified implementation - in practice you'd want to manage this better
        throw new NotSupportedException(
            "GetConnectionString is not supported in DI scenarios. Use CreateContextAsync instead.");
    }

    public async Task<TContext> CreateContextAsync<TContext>(Action<DbContextOptionsBuilder>? configure = null)
        where TContext : DbContext
    {
        await EnsureInitializedAsync();
        return await Tt.CreateContextAsync<TContext>(configure);
    }

    public async Task<TtFactory<TContext>> CreateFactoryAsync<TContext>() where TContext : DbContext
    {
        await EnsureInitializedAsync();
        return await Tt.CreateFactoryAsync<TContext>(options =>
        {
            options.Provider = _options.Provider;
            options.PoolSize = _options.PoolSize;
            options.AutoMigrate = _options.AutoMigrate;
            options.EnableSensitiveDataLogging = _options.EnableSensitiveDataLogging;
            options.DockerImage = _options.DockerImage;
            options.MemoryLimitMb = _options.MemoryLimitMb;
        });
    }

    private async Task EnsureInitializedAsync()
    {
        if (_initialized) return;

        await _initLock.WaitAsync();
        try
        {
            if (_initialized) return;

            await _pool.InitializeAsync();
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _pool.DisposeAsync();
        _initLock?.Dispose();
    }
}