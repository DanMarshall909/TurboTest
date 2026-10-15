namespace TurboTest.Benchmarks;

/// <summary>Inserted rows, separate from the mutated retained parent seed.</summary>
public sealed record WorkloadMix(int Total, int Parents, int Children, int Notes)
{
    public static WorkloadMix ForTotal(int total) => total switch
    {
        130 => new(130, 100, 20, 10),
        1300 => new(1300, 1000, 200, 100),
        13000 => new(13000, 10000, 2000, 1000),
        _ => throw new ArgumentOutOfRangeException(nameof(total), "Supported inserted row totals are 130, 1300 and 13000.")
    };

    public void RequireObserved(int parents, int children, int notes)
    {
        if (parents == 0 && children == 0 && notes == 0)
            throw new InvalidDataException("WorkloadEmpty: no inserted rows were observed.");
        if ((parents, children, notes) != (Parents, Children, Notes))
            throw new InvalidDataException($"WorkloadDistribution: expected {Parents}/{Children}/{Notes}, observed {parents}/{children}/{notes}.");
    }
}
