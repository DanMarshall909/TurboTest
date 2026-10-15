using Microsoft.EntityFrameworkCore;

namespace TurboTest.EFCore;

/// <summary>
/// Service factory for creating TurboTest resources in DI scenarios
/// </summary>
public interface ITtServiceFactory : IAsyncDisposable
{
    /// <summary>
    /// Acquires a lease from this provider's configured fixture.
    /// </summary>
    Task<TtLease<TContext>> CreateLeaseAsync<TContext>(Action<DbContextOptionsBuilder>? configure = null)
        where TContext : DbContext;

    /// <summary>
    /// Gets this provider's fixture for the context type. The provider owns its lifetime.
    /// </summary>
    Task<TtFactory<TContext>> CreateFactoryAsync<TContext>() where TContext : DbContext;
}
