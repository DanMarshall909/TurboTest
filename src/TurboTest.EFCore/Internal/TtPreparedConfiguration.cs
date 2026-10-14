using System.Text;

namespace TurboTest.EFCore.Internal;

internal static class TtPreparedConfiguration
{
    internal static void Validate(TtOptions options)
    {
        if (options.Provider != TtDatabaseProvider.PostgreSql)
            throw new ArgumentOutOfRangeException(nameof(TtOptions.Provider), "Prepared fixtures require PostgreSQL.");
        if (options.PoolSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(TtOptions.PoolSize), "The prepared slot count must be positive.");
        if (options.MemoryLimitMb <= 0)
            throw new ArgumentOutOfRangeException(nameof(TtOptions.MemoryLimitMb), "The host memory limit must be positive.");
        if (options.AutoAllocateResources)
            throw new ArgumentException("Automatic allocation is not supported by prepared fixtures.", nameof(TtOptions.AutoAllocateResources));
        if (options.EnableDynamicScaling)
            throw new ArgumentException("Dynamic scaling is not supported by prepared fixtures.", nameof(TtOptions.EnableDynamicScaling));
        if (options.ConnectionStringTemplate is not null)
            throw new ArgumentException("Prepared fixtures construct their owned connection sources.", nameof(TtOptions.ConnectionStringTemplate));
        if (options.DockerImage is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(options.DockerImage, nameof(TtOptions.DockerImage));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DatabaseName, nameof(TtOptions.DatabaseName));
        // A unique baseline suffix occupies 42 of PostgreSQL's 63 identifier bytes.
        if (Encoding.UTF8.GetByteCount(options.DatabaseName) > 21)
            throw new ArgumentException("The database prefix must be at most 21 UTF-8 bytes.", nameof(TtOptions.DatabaseName));
        if (options.Username is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(options.Username, nameof(TtOptions.Username));
            if (Encoding.UTF8.GetByteCount(options.Username) > 63)
                throw new ArgumentException("The workload role name must be at most 63 UTF-8 bytes.", nameof(TtOptions.Username));
        }
        if (options.Password is not null)
            ArgumentException.ThrowIfNullOrEmpty(options.Password, nameof(TtOptions.Password));
    }
}
