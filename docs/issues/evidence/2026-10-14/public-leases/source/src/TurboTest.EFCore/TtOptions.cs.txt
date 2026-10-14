using Microsoft.Extensions.Logging;

namespace TurboTest.EFCore;

/// <summary>
/// Configuration options for TurboTest. Can be configured via IConfiguration, IOptions, or directly.
/// </summary>
public class TtOptions
{
    internal TtOptions Snapshot() => (TtOptions)MemberwiseClone();
    /// <summary>
    /// Configuration section name for IConfiguration binding
    /// </summary>
    public const string SectionName = "TurboTest";

    /// <summary>
    /// The database provider to use. Currently only PostgreSQL is supported.
    /// Can be configured via "TurboTest:Provider" in appsettings.json
    /// </summary>
    public TtDatabaseProvider Provider { get; set; } = TtDatabaseProvider.PostgreSql;

    /// <summary>
    /// Number of containers to maintain in the pool. Defaults to 3.
    /// Can be configured via "TurboTest:PoolSize" in appsettings.json
    /// </summary>
    public int PoolSize { get; set; } = 3;

    /// <summary>
    /// Automatically run EnsureCreated when creating contexts. Defaults to true.
    /// Can be configured via "TurboTest:AutoMigrate" in appsettings.json
    /// </summary>
    public bool AutoMigrate { get; set; } = true;

    /// <summary>
    /// Enable sensitive data logging in EF Core. Defaults to true for better test error messages.
    /// Can be configured via "TurboTest:EnableSensitiveDataLogging" in appsettings.json
    /// </summary>
    public bool EnableSensitiveDataLogging { get; set; } = true;

    /// <summary>
    /// Custom Docker image to use. If null, uses default image for the provider.
    /// Can be configured via "TurboTest:DockerImage" in appsettings.json
    /// </summary>
    public string? DockerImage { get; set; }

    /// <summary>
    /// Memory limit for each container in MB. Defaults to 256.
    /// Can be configured via "TurboTest:MemoryLimitMb" in appsettings.json
    /// </summary>
    public int MemoryLimitMb { get; set; } = 256;

    /// <summary>
    /// Connection string template. Advanced users only.
    /// </summary>
    public string? ConnectionStringTemplate { get; set; }

    /// <summary>
    /// Docker host URL. If null, auto-detects based on platform.
    /// </summary>
    public string? DockerHost { get; set; }

    /// <summary>
    /// Container name prefix. Defaults to "tt".
    /// </summary>
    public string ContainerPrefix { get; set; } = "tt";

    /// <summary>
    /// Database name to create in container. Defaults to "testdb".
    /// </summary>
    public string DatabaseName { get; set; } = "testdb";

    /// <summary>
    /// Database username. Defaults based on provider.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Database password. Auto-generated if not specified.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Automatically allocate system resources optimally for testing performance.
    /// When true, ignores PoolSize and MemoryLimitMb and calculates optimal values.
    /// </summary>
    public bool AutoAllocateResources { get; set; } = false;

    /// <summary>
    /// Amount of system memory to reserve (in MB) when AutoAllocateResources is true.
    /// Defaults to 2GB to leave room for OS and other applications.
    /// </summary>
    public int ReserveMemoryMb { get; set; } = 2048;

    /// <summary>
    /// Amount of CPU cores to reserve when AutoAllocateResources is true.
    /// Defaults to 2 cores to leave room for OS and other applications.
    /// </summary>
    public int ReserveCpuCores { get; set; } = 2;

    /// <summary>
    /// Maximum number of containers to create even with auto-allocation.
    /// Prevents creating too many containers on high-spec systems.
    /// </summary>
    public int MaxAutoContainers { get; set; } = 20;

    /// <summary>
    /// Minimum log level for TurboTest internal logging. Defaults to Information.
    /// Set to Debug to see detailed resource allocation and container management logs.
    /// </summary>
    public LogLevel LogLevel { get; set; } = LogLevel.Information;

    /// <summary>
    /// Logger instance for TurboTest operations. If not provided, uses NullLogger.
    /// </summary>
    public ILogger? Logger { get; set; }

    /// <summary>
    /// Enable dynamic resource monitoring and container scaling based on system load.
    /// When enabled, TurboTest will monitor CPU and memory usage and scale containers up/down.
    /// </summary>
    public bool EnableDynamicScaling { get; set; } = false;

    /// <summary>
    /// Interval in seconds to check system resources for scaling decisions. Defaults to 30 seconds.
    /// </summary>
    public int ScalingCheckIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// CPU usage threshold (0-100) above which to scale down containers. Defaults to 80%.
    /// </summary>
    public int CpuScaleDownThreshold { get; set; } = 80;

    /// <summary>
    /// Memory usage threshold (0-100) above which to scale down containers. Defaults to 85%.
    /// </summary>
    public int MemoryScaleDownThreshold { get; set; } = 85;

    /// <summary>
    /// CPU usage threshold (0-100) below which to scale up containers. Defaults to 30%.
    /// </summary>
    public int CpuScaleUpThreshold { get; set; } = 30;

    /// <summary>
    /// Memory usage threshold (0-100) below which to scale up containers. Defaults to 50%.
    /// </summary>
    public int MemoryScaleUpThreshold { get; set; } = 50;
}

/// <summary>
/// Supported database providers
/// </summary>
public enum TtDatabaseProvider
{
    /// <summary>
    /// PostgreSQL, the currently implemented provider. Reset latency depends on workload.
    /// </summary>
    PostgreSql
}
