using Microsoft.EntityFrameworkCore;

namespace TurboTest.EFCore;

/// <summary>
/// Service factory for creating TurboTest resources in DI scenarios
/// </summary>
public interface ITtServiceFactory
{
    /// <summary>
    /// Gets a connection string for a database instance
    /// </summary>
    string GetConnectionString();

    /// <summary>
    /// Creates a configured DbContext
    /// </summary>
    Task<TtLease<TContext>> CreateLeaseAsync<TContext>(Action<DbContextOptionsBuilder>? configure = null)
        where TContext : DbContext;

    /// <summary>
    /// Creates a factory for creating multiple contexts
    /// </summary>
    Task<TtFactory<TContext>> CreateFactoryAsync<TContext>() where TContext : DbContext;
}