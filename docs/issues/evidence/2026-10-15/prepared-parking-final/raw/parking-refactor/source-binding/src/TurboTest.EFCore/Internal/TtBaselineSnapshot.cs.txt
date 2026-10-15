using System.Data;
using System.Security.Cryptography;
using Npgsql;

namespace TurboTest.EFCore.Internal;

/// <summary>Hash-based validation of the ordinary-table baseline on its owning PostgreSQL host.</summary>
internal sealed class TtBaselineSnapshot
{
    private readonly Dictionary<string, string> _signatures;

    internal TtBaselineSnapshot(IEnumerable<KeyValuePair<string, string>> signatures) =>
        _signatures = signatures.ToDictionary();

    internal TtBaselineSnapshot Schema => new(_signatures.Where(entry => !entry.Key.StartsWith("content:", StringComparison.Ordinal)));

    internal void Verify(TtBaselineSnapshot actual)
    {
        if (_signatures.Count != actual._signatures.Count ||
            _signatures.Any(expected => !actual._signatures.TryGetValue(expected.Key, out var value) || value != expected.Value))
            throw new TtLifecycleException("BaselineVerify", new InvalidOperationException("Baseline differs."));
    }

    internal static Task<TtBaselineSnapshot> CaptureAsync(NpgsqlConnection connection,
        CancellationToken cancellationToken = default) => CaptureAsync(connection, true, cancellationToken);

    internal static Task<TtBaselineSnapshot> CaptureSchemaAsync(NpgsqlConnection connection,
        CancellationToken cancellationToken = default) => CaptureAsync(connection, false, cancellationToken);

    private static async Task<TtBaselineSnapshot> CaptureAsync(NpgsqlConnection connection,
        bool includeContents, CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        await using (var settings = new NpgsqlCommand(
            "SET TRANSACTION READ ONLY; SET LOCAL row_security = off; SET LOCAL work_mem = '4MB'; SET LOCAL search_path = pg_catalog", connection, transaction))
            await settings.ExecuteNonQueryAsync(cancellationToken);

        await using (var supported = new NpgsqlCommand("""
            WITH namespaces AS (
              SELECT oid FROM pg_namespace WHERE nspname !~ '^pg_' AND nspname <> 'information_schema'
            ), relations AS (
              SELECT c.* FROM pg_class c JOIN namespaces n ON n.oid = c.relnamespace
            ) SELECT
              EXISTS (SELECT FROM relations WHERE relkind NOT IN ('r', 'i', 'S') OR relpersistence <> 'p'
                OR relrowsecurity OR relforcerowsecurity)
              OR EXISTS (SELECT FROM pg_proc p JOIN namespaces n ON n.oid = p.pronamespace)
              OR EXISTS (SELECT FROM pg_type t JOIN namespaces n ON n.oid = t.typnamespace
                WHERE t.typtype NOT IN ('b', 'c') OR (t.typtype = 'b' AND t.typelem = 0))
              OR EXISTS (SELECT FROM pg_trigger t JOIN relations r ON r.oid = t.tgrelid WHERE NOT t.tgisinternal)
              OR EXISTS (SELECT FROM pg_rewrite w JOIN relations r ON r.oid = w.ev_class)
              OR EXISTS (SELECT FROM pg_inherits i JOIN relations r ON r.oid = i.inhrelid)
              OR EXISTS (SELECT FROM pg_extension WHERE extname <> 'plpgsql')
              OR EXISTS (SELECT FROM pg_default_acl)
              OR EXISTS (SELECT FROM pg_largeobject_metadata)
              OR EXISTS (SELECT FROM pg_publication)
              OR EXISTS (SELECT FROM pg_collation c JOIN namespaces n ON n.oid = c.collnamespace)
              OR EXISTS (SELECT FROM pg_policy p JOIN relations r ON r.oid = p.polrelid)
              OR EXISTS (SELECT FROM pg_statistic_ext s JOIN namespaces n ON n.oid = s.stxnamespace)
              OR EXISTS (SELECT FROM pg_ts_config c JOIN namespaces n ON n.oid = c.cfgnamespace)
              OR EXISTS (SELECT FROM pg_ts_dict d JOIN namespaces n ON n.oid = d.dictnamespace)
              OR EXISTS (SELECT FROM pg_ts_parser p JOIN namespaces n ON n.oid = p.prsnamespace)
              OR EXISTS (SELECT FROM pg_ts_template t JOIN namespaces n ON n.oid = t.tmplnamespace)
              OR EXISTS (SELECT FROM pg_operator o JOIN namespaces n ON n.oid = o.oprnamespace)
              OR EXISTS (SELECT FROM pg_conversion c JOIN namespaces n ON n.oid = c.connamespace)
              OR EXISTS (SELECT FROM pg_cast c JOIN pg_type t ON t.oid IN (c.castsource, c.casttarget)
                JOIN namespaces n ON n.oid = t.typnamespace)
              OR EXISTS (SELECT FROM pg_shdescription WHERE classoid = 'pg_database'::regclass AND objoid =
                (SELECT oid FROM pg_database WHERE datname = current_database()))
              OR EXISTS (SELECT FROM pg_database WHERE datname = current_database()
                AND (datacl IS NOT NULL OR datconnlimit <> -1 OR datistemplate))
              OR EXISTS (SELECT FROM pg_db_role_setting WHERE setdatabase = 0 OR setdatabase =
                (SELECT oid FROM pg_database WHERE datname = current_database()))
            """, connection, transaction))
        {
            if ((bool)(await supported.ExecuteScalarAsync(cancellationToken))!)
                throw new TtLifecycleException("BaselineUnsupportedCatalog",
                    new NotSupportedException("The baseline contains unsupported catalog or database configuration."));
        }

        var signatures = new Dictionary<string, string>();
        // Only stable schema fields participate; physical storage and planner statistics
        // can change without changing the prepared contract.
        await using (var catalog = new NpgsqlCommand("""
            WITH entries AS (
              SELECT 'namespace:' || n.oid AS key,
                jsonb_build_array(n.nspname, n.nspowner, n.nspacl)::text AS definition
              FROM pg_namespace n WHERE n.nspname !~ '^pg_' AND n.nspname <> 'information_schema'
              UNION ALL
              SELECT 'type:' || t.oid, to_jsonb(t)::text
              FROM pg_type t JOIN pg_namespace n ON n.oid = t.typnamespace
              WHERE n.nspname !~ '^pg_' AND n.nspname <> 'information_schema'
              UNION ALL
              SELECT 'relation:' || c.oid,
                jsonb_build_array(n.nspname, c.relname, c.relkind, c.relpersistence,
                  c.relowner, c.relacl, c.reloptions, c.relrowsecurity, c.relforcerowsecurity,
                  c.relreplident, c.relam, c.reltablespace, c.relispartition,
                  pg_get_expr(c.relpartbound, c.oid))::text
              FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
              WHERE n.nspname !~ '^pg_' AND n.nspname <> 'information_schema'
              UNION ALL
              SELECT 'attribute:' || a.attrelid || ':' || a.attnum,
                jsonb_build_array(a.attname, a.atttypid, a.atttypmod, a.attcollation,
                  a.attnotnull, a.attidentity, a.attgenerated, a.attisdropped,
                  a.attstorage, a.attcompression, a.attacl, a.attoptions,
                  pg_get_expr(d.adbin, d.adrelid))::text
              FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid
                JOIN pg_namespace n ON n.oid = c.relnamespace
                LEFT JOIN pg_attrdef d ON d.adrelid = a.attrelid AND d.adnum = a.attnum
              WHERE a.attnum > 0 AND n.nspname !~ '^pg_' AND n.nspname <> 'information_schema'
              UNION ALL
              SELECT 'constraint:' || c.oid, to_jsonb(c)::text
              FROM pg_constraint c JOIN pg_namespace n ON n.oid = c.connamespace
              WHERE n.nspname !~ '^pg_' AND n.nspname <> 'information_schema'
              UNION ALL
              SELECT 'index:' || i.indexrelid, to_jsonb(i)::text
              FROM pg_index i JOIN pg_class c ON c.oid = i.indexrelid
                JOIN pg_namespace n ON n.oid = c.relnamespace
              WHERE n.nspname !~ '^pg_' AND n.nspname <> 'information_schema'
              UNION ALL
              SELECT 'sequence-definition:' || s.seqrelid, to_jsonb(s)::text
              FROM pg_sequence s JOIN pg_class c ON c.oid = s.seqrelid
                JOIN pg_namespace n ON n.oid = c.relnamespace
              WHERE n.nspname !~ '^pg_' AND n.nspname <> 'information_schema'
              UNION ALL
              SELECT 'sequence-ownership:' || d.objid || ':' || d.refobjid || ':' || d.refobjsubid,
                to_jsonb(d)::text
              FROM pg_depend d JOIN pg_sequence s ON d.classid = 'pg_class'::regclass AND d.objid = s.seqrelid
                JOIN pg_class c ON c.oid = s.seqrelid JOIN pg_namespace n ON n.oid = c.relnamespace
              WHERE d.refclassid = 'pg_class'::regclass AND d.deptype IN ('a', 'i')
                AND n.nspname !~ '^pg_' AND n.nspname <> 'information_schema'
              UNION ALL
              SELECT 'comment:' || d.classoid || ':' || d.objoid || ':' || d.objsubid, to_jsonb(d)::text
              FROM pg_description d WHERE
                (d.classoid = 'pg_class'::regclass AND d.objoid IN (
                  SELECT c.oid FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                  WHERE n.nspname !~ '^pg_' AND n.nspname <> 'information_schema'))
                OR (d.classoid = 'pg_namespace'::regclass AND d.objoid IN (
                  SELECT oid FROM pg_namespace WHERE nspname !~ '^pg_' AND nspname <> 'information_schema'))
                OR (d.classoid = 'pg_constraint'::regclass AND d.objoid IN (
                  SELECT c.oid FROM pg_constraint c JOIN pg_namespace n ON n.oid = c.connamespace
                  WHERE n.nspname !~ '^pg_' AND n.nspname <> 'information_schema'))
                OR (d.classoid = 'pg_type'::regclass AND d.objoid IN (
                  SELECT t.oid FROM pg_type t JOIN pg_namespace n ON n.oid = t.typnamespace
                  WHERE n.nspname !~ '^pg_' AND n.nspname <> 'information_schema'))
            ) SELECT key, convert_to(definition, 'UTF8') FROM entries ORDER BY key
            """, connection, transaction))
        await using (var reader = await catalog.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (signatures.Count >= 10000)
                    throw new TtLifecycleException("BaselineLimit", new InvalidOperationException("Too many baseline objects."));
                var key = reader.GetString(0);
                await using var bytes = reader.GetStream(1);
                signatures.Add(key, Convert.ToHexString(await SHA256.HashDataAsync(bytes, cancellationToken)));
            }
        }

        if (!includeContents)
        {
            await transaction.CommitAsync(cancellationToken);
            return new TtBaselineSnapshot(signatures);
        }

        var relations = new List<(uint Oid, string Schema, string Name, char Kind)>();
        await using (var enumerate = new NpgsqlCommand("""
            SELECT c.oid, n.nspname, c.relname, c.relkind
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind IN ('r', 'S') AND c.relpersistence = 'p'
              AND n.nspname !~ '^pg_' AND n.nspname <> 'information_schema'
            ORDER BY c.oid
            """, connection, transaction))
        await using (var reader = await enumerate.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (relations.Count >= 10000)
                    throw new TtLifecycleException("BaselineLimit", new InvalidOperationException("Too many baseline relations."));
                relations.Add((reader.GetFieldValue<uint>(0), reader.GetString(1), reader.GetString(2), reader.GetChar(3)));
            }
        }
        foreach (var relation in relations)
        {
            var name = $"{Quote(relation.Schema)}.{Quote(relation.Name)}";
            var sql = relation.Kind == 'S'
                ? $"SELECT sha256(convert_to(jsonb_build_array(last_value, is_called)::text, 'UTF8')) FROM {name}"
                : $"SELECT sha256(record_send(t)) AS h FROM ONLY {name} AS t ORDER BY h";
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long count = 0;
            while (await reader.ReadAsync(cancellationToken))
            {
                digest.AppendData(reader.GetFieldValue<byte[]>(0));
                count++;
            }
            signatures.Add($"content:{relation.Oid}", $"{count}:{Convert.ToHexString(digest.GetHashAndReset())}");
        }
        await transaction.CommitAsync(cancellationToken);
        return new TtBaselineSnapshot(signatures);
    }

    private static string Quote(string identifier) => new NpgsqlCommandBuilder().QuoteIdentifier(identifier);
}
