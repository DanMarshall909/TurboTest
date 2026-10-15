namespace TurboTest.Benchmarks;

/// <summary>Accepts verified growth identities while enforcing stable reuse and a finite physical cap.</summary>
public sealed class BenchmarkDatabaseRegistry
{
    private readonly object _gate = new();
    private readonly int _capacity;
    private readonly Dictionary<string, uint> _known = new(StringComparer.Ordinal);
    public BenchmarkDatabaseRegistry(int capacity) { _capacity = capacity; }

    public void Observe(DatabaseIdentity identity)
    {
        lock (_gate)
        {
            if (_known.TryGetValue(identity.Name, out var oid))
            {
                if (oid != identity.Oid) throw new InvalidDataException("DatabaseReuse: a known database changed its OID.");
                return;
            }
            if (_known.Values.Contains(identity.Oid)) throw new InvalidDataException("DatabaseIdentity: one OID has multiple names.");
            if (_known.Count >= _capacity) throw new InvalidDataException("DatabaseCapacity: verified identities exceed the physical cap.");
            _known.Add(identity.Name, identity.Oid);
        }
    }

    public DatabaseIdentity[] Snapshot
    {
        get { lock (_gate) return _known.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new DatabaseIdentity(pair.Key, pair.Value)).ToArray(); }
    }
}
