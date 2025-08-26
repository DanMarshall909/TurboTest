namespace TurboTest.EFCore;

/// <summary>
/// Configuration options for TurboTest
/// </summary>
public class TtOptions
{
    /// <summary>
    /// The database provider to use. Defaults to PostgreSQL.
    /// </summary>
    public TtDatabaseProvider Provider { get; set; } = TtDatabaseProvider.PostgreSql;

    /// <summary>
    /// Number of containers to maintain in the pool. Defaults to 3.
    /// </summary>
    public int PoolSize { get; set; } = 3;

    /// <summary>
    /// Automatically run migrations when creating contexts. Defaults to true.
    /// </summary>
    public bool AutoMigrate { get; set; } = true;

    /// <summary>
    /// Enable sensitive data logging in EF Core. Defaults to false.
    /// </summary>
    public bool EnableSensitiveDataLogging { get; set; } = false;

    /// <summary>
    /// Custom Docker image to use. If null, uses default image for the provider.
    /// </summary>
    public string? DockerImage { get; set; }

    /// <summary>
    /// Memory limit for each container in MB. Defaults to 256.
    /// </summary>
    public int MemoryLimitMb { get; set; } = 256;
}

/// <summary>
/// Supported database providers
/// </summary>
public enum TtDatabaseProvider
{
    /// <summary>
    /// PostgreSQL (fastest reset time ~50ms)
    /// </summary>
    PostgreSql,
    
    /// <summary>
    /// SQL Server (reset time ~100ms) - Coming soon
    /// </summary>
    SqlServer,
    
    /// <summary>
    /// MySQL (reset time ~200ms) - Coming soon
    /// </summary>
    MySql
}