namespace TurboTest.EFCore;

/// <summary>Immutable counts of workload reservations, including unavailable capacity.</summary>
public sealed record TtPoolSnapshot(Guid OwnerId, int SlotLimit, int Preparing, int Ready, int Leased,
    int WaitingResets, int ExecutingResets, int Quarantined, int Parked, int Retiring, int QueuedAcquisitions)
{
    public int WaitingAdministratorConnections { get; init; }
    public int ExecutingAdministratorConnections { get; init; }
    /// <summary>Idle reservations claimed for bounded connection-pool closure.</summary>
    public int Parking { get; init; }
    /// <summary>Retained reservations waiting for bounded reopening and validation.</summary>
    public int Rewarming { get; init; }
    /// <summary>Occupied shared reset/deletion/parking permits, including maintenance outside row reset.</summary>
    public int ExecutingCleanup { get; init; }
    /// <summary>Current admitted warm target, separate from the physical reservation ceiling.</summary>
    public int WarmTarget { get; init; }
    public int Reserved => Preparing + Ready + Leased + WaitingResets + ExecutingResets + Quarantined + Parking + Parked + Rewarming + Retiring;
}
