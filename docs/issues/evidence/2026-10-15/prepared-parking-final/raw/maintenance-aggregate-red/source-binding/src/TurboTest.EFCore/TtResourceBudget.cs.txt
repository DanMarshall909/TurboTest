namespace TurboTest.EFCore;

/// <summary>Immutable ceilings for TurboTest-owned fixture resources.</summary>
public sealed record TtResourceBudget(int WorkloadSlotLimit, int ContainerLimit, int MaxConnectionsPerDatabase,
    int AdministratorConnectionAllowance, int PreparationConcurrency, int ResetConcurrency, int FixedInfrastructureDatabases)
{
    /// <summary>Workload databases plus the prepared owner's baseline/control databases.</summary>
    public long MaximumOwnedDatabases => (long)WorkloadSlotLimit + FixedInfrastructureDatabases;

    /// <summary>Managed workload connections plus administrator/bootstrap connections.</summary>
    public long MaximumManagedConnections => (long)WorkloadSlotLimit * MaxConnectionsPerDatabase + AdministratorConnectionAllowance;

    internal static TtResourceBudget Create(TtOptions options, bool prepared)
    {
        ValidateDeadline(options.StartupTimeout, nameof(TtOptions.StartupTimeout));
        ValidateDeadline(options.DrainTimeout, nameof(TtOptions.DrainTimeout));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.PoolSize, nameof(TtOptions.PoolSize));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.PreparationConcurrency, nameof(TtOptions.PreparationConcurrency));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.ResetConcurrency, nameof(TtOptions.ResetConcurrency));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxConnectionsPerDatabase, nameof(TtOptions.MaxConnectionsPerDatabase));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxAdministratorConnections, nameof(TtOptions.MaxAdministratorConnections));
        var requiredContainers = prepared ? 1 : options.PoolSize;
        var containers = options.MaxContainers ?? requiredContainers;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(containers, nameof(TtOptions.MaxContainers));
        if (containers < requiredContainers)
            throw new ArgumentException("The container ceiling cannot host the fixed workload capacity.", nameof(TtOptions.MaxContainers));
        return new TtResourceBudget(options.PoolSize, containers, options.MaxConnectionsPerDatabase,
            prepared ? options.MaxAdministratorConnections : 0, options.PreparationConcurrency,
            options.ResetConcurrency, prepared ? 2 : 0);
    }

    private static void ValidateDeadline(TimeSpan value, string name)
    {
        if (value <= TimeSpan.Zero || value.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(name, "The deadline must be positive and supported by the runtime timer.");
    }
}
