using Microsoft.EntityFrameworkCore;
using Npgsql;
using TurboTest.EFCore;
using static TurboTest.Tests.SeededBaselineTests;

namespace TurboTest.Tests;

public class BaselineRestoreTests
{
    [Theory]
    [InlineData("sequence")]
    [InlineData("validation")]
    [Trait("Category", "PostgreSQL")]
    public async Task Failed_sequence_restoration_or_post_commit_validation_cannot_publish_a_dirty_slot(string boundary)
    {
        var inject = true;
        long? valueAfterRollback = null;
        var baseline = new TtBaselineDefinition<TestDbContext>("failed-reset-v1", TtSchemaInitialization.EnsureCreated,
            async (context, cancellation) =>
            {
                context.Users.Add(new User { Email = "failure-seed@example.com" });
                await context.SaveChangesAsync(cancellation);
            });
        await using var fixture = await Tt.CreatePreparedFactoryAsync(new TtOptions { PoolSize = 1 }, baseline, default,
            afterSequenceRestore: async (connection, transaction) =>
            {
                if (!inject || boundary != "sequence") return;
                await transaction.RollbackAsync();
                await using var command = new NpgsqlCommand("SELECT last_value FROM \"Users_Id_seq\"", connection);
                valueAfterRollback = (long)(await command.ExecuteScalarAsync())!;
                throw new IOException("Injected failure with secret-payload after setval.");
            },
            afterRowCommit: async connection =>
            {
                if (!inject || boundary != "validation") return;
                await using var command = new NpgsqlCommand("DELETE FROM \"Users\"", connection);
                await command.ExecuteNonQueryAsync();
            });
        var lease = await fixture.CreateLeaseAsync();
        var original = await DatabaseOidAsync(lease.Context);
        var originalName = lease.Context.Database.GetDbConnection().Database;
        await lease.Context.Users.ExecuteUpdateAsync(update => update.SetProperty(user => user.Email, "changed-payload"));
        lease.Context.Users.Add(new User { Email = "added-payload" });
        await lease.Context.SaveChangesAsync();
        var error = await Record.ExceptionAsync(() => lease.DisposeAsync().AsTask());
        inject = false;
        var failure = Assert.IsType<TtLifecycleException>(error);
        Assert.Equal("BaselineRowReset", failure.Stage);
        Assert.DoesNotContain("secret-payload", failure.ToString());
        Assert.DoesNotContain("changed-payload", failure.ToString());
        if (boundary == "sequence") Assert.Equal(1L, valueAfterRollback);
        Assert.Same(failure, await Record.ExceptionAsync(() => lease.DisposeAsync().AsTask()));
        await using var recovered = await fixture.CreateLeaseAsync();
        Assert.NotEqual(original, await DatabaseOidAsync(recovered.Context));
        var seed = await recovered.Context.Users.SingleAsync();
        Assert.Equal((1, "failure-seed@example.com"), (seed.Id, seed.Email));
        var connection = (NpgsqlConnection)recovered.Context.Database.GetDbConnection();
        await using var database = new NpgsqlCommand("SELECT count(*) FROM pg_database WHERE datname = $1", connection);
        database.Parameters.Add(new NpgsqlParameter { Value = originalName });
        Assert.Equal(0L, await database.ExecuteScalarAsync());
        var added = new User { Email = "after-failure@example.com" };
        recovered.Context.Users.Add(added);
        await recovered.Context.SaveChangesAsync();
        Assert.Equal(2, added.Id);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_foreign_key_cycle_uses_template_recreation_without_partial_row_cleanup()
    {
        var baseline = new TtBaselineDefinition<TestDbContext>("cycle-reset-v1", TtSchemaInitialization.Caller,
            async (context, cancellation) =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync(cancellation);
                await context.Database.ExecuteSqlRawAsync("""
                    CREATE TABLE cycle_left(id integer PRIMARY KEY, right_id integer NOT NULL, label text);
                    CREATE TABLE cycle_right(id integer PRIMARY KEY, left_id integer NOT NULL REFERENCES cycle_left DEFERRABLE INITIALLY DEFERRED);
                    ALTER TABLE cycle_left ADD FOREIGN KEY(right_id) REFERENCES cycle_right DEFERRABLE INITIALLY DEFERRED;
                    INSERT INTO cycle_left VALUES (7, 11, 'cycle-seed');
                    INSERT INTO cycle_right VALUES (11, 7);
                    """, cancellation);
                await transaction.CommitAsync(cancellation);
            });
        await using var fixture = await Tt.CreateFactoryAsync(baseline, options => options.PoolSize = 1);
        uint original;
        await using (var lease = await fixture.CreateLeaseAsync())
        {
            original = await DatabaseOidAsync(lease.Context);
            await lease.Context.Database.ExecuteSqlRawAsync("UPDATE cycle_left SET label = 'changed'");
        }
        await using var restored = await fixture.CreateLeaseAsync();
        Assert.NotEqual(original, await DatabaseOidAsync(restored.Context));
        await using var rows = new NpgsqlCommand("SELECT l.id, r.id, l.label FROM cycle_left l JOIN cycle_right r ON r.id=l.right_id AND r.left_id=l.id",
            (NpgsqlConnection)restored.Context.Database.GetDbConnection());
        await using var reader = await rows.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal((7, 11, "cycle-seed"), (reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2)));
        Assert.False(await reader.ReadAsync());
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_seed_above_the_binary_cache_budget_uses_verified_recreation()
    {
        const int length = 17 * 1024 * 1024;
        var baseline = new TtBaselineDefinition<TestDbContext>("large-reset-v1", TtSchemaInitialization.Caller,
            (context, cancellation) => context.Database.ExecuteSqlRawAsync($"CREATE TABLE large_seed(id integer PRIMARY KEY, payload text); INSERT INTO large_seed VALUES (7, repeat('x', {length}))", cancellation));
        await using var fixture = await Tt.CreateFactoryAsync(baseline, options => options.PoolSize = 1);
        uint original;
        await using (var lease = await fixture.CreateLeaseAsync())
        {
            original = await DatabaseOidAsync(lease.Context);
            await lease.Context.Database.ExecuteSqlRawAsync("UPDATE large_seed SET payload = 'changed'");
        }
        await using var restored = await fixture.CreateLeaseAsync();
        Assert.NotEqual(original, await DatabaseOidAsync(restored.Context));
        await using var rows = new NpgsqlCommand("SELECT id, length(payload), sha256(convert_to(payload, 'UTF8')) FROM large_seed",
            (NpgsqlConnection)restored.Context.Database.GetDbConnection());
        await using var reader = await rows.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal((7, length), (reader.GetInt32(0), reader.GetInt32(1)));
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        var block = Enumerable.Repeat((byte)'x', 1024).ToArray();
        for (var i = 0; i < length / block.Length; i++) hash.AppendData(block);
        Assert.Equal(hash.GetHashAndReset(), reader.GetFieldValue<byte[]>(2));
        Assert.False(await reader.ReadAsync());
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Binary_restore_handles_generated_columns_and_cross_schema_foreign_keys()
    {
        var baseline = new TtBaselineDefinition<TestDbContext>("binary-schema-v1", TtSchemaInitialization.Caller,
            (context, cancellation) => context.Database.ExecuteSqlRawAsync("""
                CREATE SCHEMA "tenant odd";
                CREATE TABLE "tenant odd"."Parent" (
                  "Id" integer GENERATED ALWAYS AS IDENTITY (START WITH 50 CACHE 5) PRIMARY KEY,
                  "Label" text NOT NULL, "Bytes" bytea, "Numbers" integer[], "Document" jsonb,
                  "Length" integer GENERATED ALWAYS AS (length("Label")) STORED);
                CREATE TABLE public.binary_child(id integer PRIMARY KEY, parent_id integer REFERENCES "tenant odd"."Parent");
                INSERT INTO "tenant odd"."Parent" ("Id", "Label", "Bytes", "Numbers", "Document") OVERRIDING SYSTEM VALUE
                  VALUES (43, 'héllo-seed', decode('00ff017f', 'hex'), ARRAY[7,11,23], jsonb_build_object('value',37));
                INSERT INTO public.binary_child VALUES (19, 43);
                """, cancellation));
        await using var fixture = await Tt.CreateFactoryAsync(baseline, options => options.PoolSize = 1);
        uint original = 0;
        for (var iteration = 0; iteration < 2; iteration++)
        {
            await using var lease = await fixture.CreateLeaseAsync();
            var oid = await DatabaseOidAsync(lease.Context);
            if (iteration == 0) original = oid;
            else Assert.Equal(original, oid);
            var connection = (NpgsqlConnection)lease.Context.Database.GetDbConnection();
            await using (var command = new NpgsqlCommand("""
                SELECT p."Id", p."Label", p."Bytes", p."Numbers", p."Document"->>'value', p."Length", c.id
                FROM "tenant odd"."Parent" p JOIN public.binary_child c ON c.parent_id=p."Id"
                """, connection))
            await using (var reader = await command.ExecuteReaderAsync())
            {
                Assert.True(await reader.ReadAsync());
                Assert.Equal((43, "héllo-seed", "37", 10, 19),
                    (reader.GetInt32(0), reader.GetString(1), reader.GetString(4), reader.GetInt32(5), reader.GetInt32(6)));
                Assert.Equal(new byte[] { 0, 255, 1, 127 }, reader.GetFieldValue<byte[]>(2));
                Assert.Equal(new[] { 7, 11, 23 }, reader.GetFieldValue<int[]>(3));
                Assert.False(await reader.ReadAsync());
            }
            await using (var insert = new NpgsqlCommand("INSERT INTO \"tenant odd\".\"Parent\" (\"Label\") VALUES ('generated') RETURNING \"Id\"", connection))
                Assert.Equal(50, await insert.ExecuteScalarAsync());
            await lease.Context.Database.ExecuteSqlRawAsync("DELETE FROM binary_child; UPDATE \"tenant odd\".\"Parent\" SET \"Label\"='changed', \"Bytes\"=NULL, \"Numbers\"=ARRAY[91], \"Document\"=jsonb_build_object('value',99)");
        }
    }

    [Theory]
    [InlineData("CREATE TABLE unexpected_table(id integer)")]
    [InlineData("ALTER TABLE \"Users\" ADD COLUMN unexpected_column text")]
    [InlineData("CREATE SCHEMA unexpected_schema")]
    [InlineData("CREATE VIEW unexpected_view AS SELECT 1 AS value")]
    [InlineData("CREATE FUNCTION unexpected_function() RETURNS integer LANGUAGE sql AS 'SELECT 1'")]
    [InlineData("CREATE TYPE unexpected_type AS ENUM ('one', 'two')")]
    [InlineData("GRANT SELECT ON \"Users\" TO PUBLIC")]
    [Trait("Category", "PostgreSQL")]
    public Task Changed_or_unsupported_catalog_uses_verified_template_recreation(string change) => AssertRecreatedCatalogAsync(change);

    [Theory]
    [InlineData("CREATE OPERATOR public.## (FUNCTION = pg_catalog.int4pl, LEFTARG = integer, RIGHTARG = integer)")]
    [InlineData("CREATE CONVERSION public.unexpected_conversion FOR 'UTF8' TO 'LATIN1' FROM pg_catalog.utf8_to_iso8859_1")]
    [InlineData("DO $$ BEGIN EXECUTE format('COMMENT ON DATABASE %I IS %L', current_database(), 'unexpected-database-comment'); END $$")]
    [Trait("Category", "PostgreSQL")]
    public Task Additional_catalog_objects_require_verified_template_recreation(string change) => AssertRecreatedCatalogAsync(change);

    [Theory]
    [InlineData("DO $$ BEGIN EXECUTE format('ALTER DATABASE %I IS_TEMPLATE true', current_database()); END $$")]
    [InlineData("CREATE CAST (public.\"Users\" AS text) WITH INOUT")]
    [InlineData("REVOKE USAGE ON TYPE public.\"Users\" FROM PUBLIC")]
    [Trait("Category", "PostgreSQL")]
    public Task Changed_database_or_row_type_state_requires_template_recreation(string change) => AssertRecreatedCatalogAsync(change);

    private static async Task AssertRecreatedCatalogAsync(string change)
    {
        var baseline = new TtBaselineDefinition<TestDbContext>("schema-fallback-v1", TtSchemaInitialization.EnsureCreated,
            async (context, cancellation) =>
            {
                context.Users.Add(new User { Email = "schema-seed@example.com" });
                await context.SaveChangesAsync(cancellation);
            });
        await using var fixture = await Tt.CreateFactoryAsync(baseline, options => options.PoolSize = 1);
        uint original;
        await using (var lease = await fixture.CreateLeaseAsync())
        {
            original = await DatabaseOidAsync(lease.Context);
            await lease.Context.Database.ExecuteSqlRawAsync(change);
            await lease.Context.Users.ExecuteDeleteAsync();
        }
        await using var restored = await fixture.CreateLeaseAsync();
        var seed = await restored.Context.Users.SingleAsync();
        Assert.Equal((1, "schema-seed@example.com"), (seed.Id, seed.Email));
        Assert.NotEqual(original, await DatabaseOidAsync(restored.Context));
        var connection = (NpgsqlConnection)restored.Context.Database.GetDbConnection();
        await using var objects = new NpgsqlCommand("""
            SELECT count(*) FROM (
              SELECT nspname AS name FROM pg_namespace
              UNION ALL SELECT relname FROM pg_class
              UNION ALL SELECT proname FROM pg_proc
              UNION ALL SELECT typname FROM pg_type
              UNION ALL SELECT attname FROM pg_attribute
              UNION ALL SELECT conname FROM pg_conversion
              UNION ALL SELECT 'unexpected_operator' FROM pg_operator o JOIN pg_namespace n ON n.oid = o.oprnamespace
                WHERE n.nspname = 'public' AND o.oprname = '##'
              UNION ALL SELECT 'unexpected_cast' FROM pg_cast c JOIN pg_type t ON t.oid = c.castsource
                JOIN pg_namespace n ON n.oid = t.typnamespace WHERE n.nspname = 'public'
            ) objects WHERE name LIKE 'unexpected_%'
            """, connection);
        Assert.Equal(0L, await objects.ExecuteScalarAsync());
        await using var database = new NpgsqlCommand("""
            SELECT datistemplate, shobj_description(oid, 'pg_database') FROM pg_database WHERE datname = current_database()
            """, connection);
        await using (var reader = await database.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.False(reader.GetBoolean(0));
            Assert.True(reader.IsDBNull(1));
        }
        var added = new User { Email = "after-recreation@example.com" };
        restored.Context.Users.Add(added);
        await restored.Context.SaveChangesAsync();
        Assert.Equal(2, added.Id);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Row_reset_restores_updated_deleted_and_inserted_seeds_in_the_same_database()
    {
        var calls = 0;
        var baseline = new TtBaselineDefinition<SeedContext>("row-restore-v1",
            TtSchemaInitialization.EnsureCreated, async (context, cancellation) =>
            {
                calls++;
                context.Parents.Add(new SeedParent { Id = 100, Name = "baseline-parent", Priority = 17 });
                context.Children.Add(new SeedChild { Id = 200, ParentId = 100, Name = "baseline-child", Enabled = true });
                context.Notes.Add(new SeedNote { Id = 300, ChildId = 200, Body = "baseline-note", Revision = 29 });
                await context.SaveChangesAsync(cancellation);
                await context.Database.ExecuteSqlRawAsync("""
                    SELECT setval(pg_get_serial_sequence('"Parents"', 'Id'), 100, true);
                    SELECT setval(pg_get_serial_sequence('"Children"', 'Id'), 200, true);
                    SELECT setval(pg_get_serial_sequence('"Notes"', 'Id'), 300, true);
                    """, cancellation);
            });
        await using var fixture = await Tt.CreateFactoryAsync(baseline, options => options.PoolSize = 1);
        uint original;
        await using (var lease = await fixture.CreateLeaseAsync())
        {
            original = await DatabaseOidAsync(lease.Context);
            await lease.Context.Parents.ExecuteUpdateAsync(update => update.SetProperty(p => p.Name, "changed").SetProperty(p => p.Priority, 91));
            await lease.Context.Notes.ExecuteDeleteAsync();
            await lease.Context.Children.ExecuteDeleteAsync();
            var parent = new SeedParent { Name = "extra-parent", Priority = 43 };
            lease.Context.Parents.Add(parent);
            await lease.Context.SaveChangesAsync();
            var child = new SeedChild { ParentId = parent.Id, Name = "extra-child", Enabled = false };
            lease.Context.Children.Add(child);
            await lease.Context.SaveChangesAsync();
            lease.Context.Notes.Add(new SeedNote { ChildId = child.Id, Body = "extra-note", Revision = 71 });
            await lease.Context.SaveChangesAsync();
        }
        await using var restored = await fixture.CreateLeaseAsync();
        var parentSeed = await restored.Context.Parents.SingleAsync();
        var childSeed = await restored.Context.Children.SingleAsync();
        var noteSeed = await restored.Context.Notes.SingleAsync();
        Assert.Equal((100, "baseline-parent", 17), (parentSeed.Id, parentSeed.Name, parentSeed.Priority));
        Assert.Equal((200, 100, "baseline-child", true), (childSeed.Id, childSeed.ParentId, childSeed.Name, childSeed.Enabled));
        Assert.Equal((300, 200, "baseline-note", 29), (noteSeed.Id, noteSeed.ChildId, noteSeed.Body, noteSeed.Revision));
        var nextParent = new SeedParent { Name = "next-parent" };
        restored.Context.Parents.Add(nextParent);
        await restored.Context.SaveChangesAsync();
        var nextChild = new SeedChild { ParentId = nextParent.Id, Name = "next-child" };
        restored.Context.Children.Add(nextChild);
        await restored.Context.SaveChangesAsync();
        var nextNote = new SeedNote { ChildId = nextChild.Id, Body = "next-note" };
        restored.Context.Notes.Add(nextNote);
        await restored.Context.SaveChangesAsync();
        Assert.Equal((101, 201, 301), (nextParent.Id, nextChild.Id, nextNote.Id));
        Assert.Equal(1, calls);
        Assert.Equal(original, await DatabaseOidAsync(restored.Context));
    }

    private static async Task<uint> DatabaseOidAsync(DbContext context)
    {
        await context.Database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("SELECT oid FROM pg_database WHERE datname = current_database()",
            (NpgsqlConnection)context.Database.GetDbConnection());
        return (uint)(await command.ExecuteScalarAsync())!;
    }
}
