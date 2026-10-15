using Microsoft.EntityFrameworkCore;
using TurboTest.EFCore;

namespace TurboTest.Benchmarks;

/// <summary>Three related seeded tables shared by the runner and native calibration.</summary>
public static class BenchmarkWorkload
{
    public const string Version = "benchmark-parent-child-note-v1";

    public static TtBaselineDefinition<BenchmarkContext> Baseline { get; } = new(Version, TtSchemaInitialization.EnsureCreated,
        async (context, token) =>
        {
            context.Parents.Add(new BenchmarkParent { Id = 100, Name = "baseline-parent", Priority = 17 });
            context.Children.Add(new BenchmarkChild { Id = 200, ParentId = 100, Name = "baseline-child", Enabled = true });
            context.Notes.Add(new BenchmarkNote { Id = 300, ChildId = 200, Body = "baseline-note", Revision = 29 });
            await context.SaveChangesAsync(token);
            await context.Database.ExecuteSqlRawAsync("""
                SELECT setval(pg_get_serial_sequence('"Parents"', 'Id'),100,true);
                SELECT setval(pg_get_serial_sequence('"Children"', 'Id'),200,true);
                SELECT setval(pg_get_serial_sequence('"Notes"', 'Id'),300,true);
                """, token);
        });

    public static async Task LoadAsync(BenchmarkContext context, WorkloadMix mix, string marker,
        CancellationToken cancellationToken = default)
    {
        await context.Notes.ExecuteDeleteAsync(cancellationToken);
        await context.Children.ExecuteDeleteAsync(cancellationToken);
        await context.Parents.ExecuteUpdateAsync(update => update.SetProperty(row => row.Name, "changed-seed")
            .SetProperty(row => row.Priority, 91), cancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Parents"("Name","Priority")
            SELECT {marker}||'-parent-'||i,31+i FROM generate_series(1,{mix.Parents}) i
            """, cancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Children"("ParentId","Name","Enabled")
            SELECT "Id",{marker}||'-child-'||"Id",false FROM "Parents"
            WHERE left("Name",length({marker}))={marker} ORDER BY "Id" LIMIT {mix.Children}
            """, cancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Notes"("ChildId","Body","Revision")
            SELECT "Id",{marker}||'-note-'||"Id",71 FROM "Children"
            WHERE left("Name",length({marker}))={marker} ORDER BY "Id" LIMIT {mix.Notes}
            """, cancellationToken);
    }

    public static async Task VerifyBaselineAsync(BenchmarkContext context, CancellationToken cancellationToken = default)
    {
        var parents = await context.Parents.AsNoTracking().Take(2).ToArrayAsync(cancellationToken);
        var children = await context.Children.AsNoTracking().Take(2).ToArrayAsync(cancellationToken);
        var notes = await context.Notes.AsNoTracking().Take(2).ToArrayAsync(cancellationToken);
        if (parents.Length != 1 || children.Length != 1 || notes.Length != 1 ||
            (parents[0].Id, parents[0].Name, parents[0].Priority) != (100, "baseline-parent", 17) ||
            (children[0].Id, children[0].ParentId, children[0].Name, children[0].Enabled) != (200, 100, "baseline-child", true) ||
            (notes[0].Id, notes[0].ChildId, notes[0].Body, notes[0].Revision) != (300, 200, "baseline-note", 29))
            throw new InvalidDataException("BaselineMismatch: seed identity, values, relationships or row counts differ.");
    }

    public static async Task ValidateMutatedAsync(BenchmarkContext context, WorkloadMix mix, string marker,
        CancellationToken cancellationToken = default)
    {
        var parents = await context.Parents.CountAsync(row => row.Name.StartsWith(marker), cancellationToken);
        var children = await context.Children.CountAsync(row => row.Name.StartsWith(marker), cancellationToken);
        var notes = await context.Notes.CountAsync(row => row.Body.StartsWith(marker), cancellationToken);
        mix.RequireObserved(parents, children, notes);
        var seed = await context.Parents.AsNoTracking().SingleOrDefaultAsync(row => row.Id == 100, cancellationToken);
        if (seed is null || (seed.Name, seed.Priority) != ("changed-seed", 91) ||
            await context.Children.AnyAsync(row => row.Id == 200, cancellationToken) ||
            await context.Notes.AnyAsync(row => row.Id == 300, cancellationToken))
            throw new InvalidDataException("SeedMutation: retained parent update or deleted child/note seeds differ.");
        if (await context.Parents.CountAsync(cancellationToken) != parents + 1 ||
            await context.Children.CountAsync(cancellationToken) != children || await context.Notes.CountAsync(cancellationToken) != notes)
            throw new InvalidDataException("ForeignMarker: rows outside this operation remain in the database.");
        if (await context.Parents.Where(row => row.Name.StartsWith(marker)).MinAsync(row => row.Id, cancellationToken) != 101 ||
            await context.Children.MinAsync(row => row.Id, cancellationToken) != 201 ||
            await context.Notes.MinAsync(row => row.Id, cancellationToken) != 301)
            throw new InvalidDataException("Sequence: next inserted identities differ from 101/201/301.");
    }
}

public sealed class BenchmarkContext(DbContextOptions<BenchmarkContext> options) : DbContext(options)
{
    public DbSet<BenchmarkParent> Parents => Set<BenchmarkParent>();
    public DbSet<BenchmarkChild> Children => Set<BenchmarkChild>();
    public DbSet<BenchmarkNote> Notes => Set<BenchmarkNote>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<BenchmarkParent>().ToTable("Parents");
        model.Entity<BenchmarkChild>().ToTable("Children").HasOne<BenchmarkParent>().WithMany().HasForeignKey(row => row.ParentId);
        model.Entity<BenchmarkNote>().ToTable("Notes").HasOne<BenchmarkChild>().WithMany().HasForeignKey(row => row.ChildId);
    }
}

public sealed class BenchmarkParent { public int Id { get; set; } public string Name { get; set; } = ""; public int Priority { get; set; } }
public sealed class BenchmarkChild { public int Id { get; set; } public int ParentId { get; set; } public string Name { get; set; } = ""; public bool Enabled { get; set; } }
public sealed class BenchmarkNote { public int Id { get; set; } public int ChildId { get; set; } public string Body { get; set; } = ""; public int Revision { get; set; } }
