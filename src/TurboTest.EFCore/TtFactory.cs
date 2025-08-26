using Microsoft.EntityFrameworkCore;
using TurboTest.EFCore.Internal;

namespace TurboTest.EFCore;

/// <summary>
/// Factory for creating multiple DbContext instances with shared configuration.
/// Useful for test fixtures.
/// </summary>
public class TtFactory<TContext> : IAsyncDisposable where TContext : DbContext
{
    private readonly TtPool _pool;
    private readonly TtOptions _options;
    private readonly List<TtDatabase> _activeDatabases = new();

    internal TtFactory(TtPool pool, TtOptions options)
    {
        _pool = pool;
        _options = options;
    }

    /// <summary>
    /// Creates a new context instance with a fresh database.
    /// </summary>
    public async Task<TContext> CreateContextAsync(Action<DbContextOptionsBuilder>? configure = null)
    {
        var database = await _pool.GetDatabaseAsync();
        _activeDatabases.Add(database);

        var optionsBuilder = new DbContextOptionsBuilder<TContext>();
        optionsBuilder.UseNpgsql(database.ConnectionString);

        if (_options.EnableSensitiveDataLogging)
        {
            optionsBuilder.EnableSensitiveDataLogging();
        }

        optionsBuilder.AddInterceptors(new TtCleanupInterceptor(database));
        configure?.Invoke(optionsBuilder);

        var context = Activator.CreateInstance(
            typeof(TContext),
            optionsBuilder.Options) as TContext
            ?? throw new InvalidOperationException($"Could not create instance of {typeof(TContext).Name}");

        if (_options.AutoMigrate)
        {
            await context.Database.EnsureCreatedAsync();
        }

        return context;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var database in _activeDatabases)
        {
            await database.DisposeAsync();
        }
        _activeDatabases.Clear();
    }
}