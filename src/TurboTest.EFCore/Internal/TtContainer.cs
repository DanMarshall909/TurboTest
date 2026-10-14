using Docker.DotNet;
using Docker.DotNet.Models;
using Npgsql;

namespace TurboTest.EFCore.Internal;

/// <summary>
/// Represents a single containerized database instance.
/// </summary>
internal sealed class TtContainer : IAsyncDisposable
{
    private readonly DockerClient _dockerClient;

    private NpgsqlDataSource? _source;
    public NpgsqlDataSource DataSource => _source ?? throw new InvalidOperationException("The container has not completed database preparation.");

    public string Id { get; }

    public TtContainer(string id, DockerClient dockerClient)
    {
        Id = id;
        _dockerClient = dockerClient;
    }

    internal void AttachDataSource(NpgsqlDataSource source)
    {
        if (_source is not null) throw new InvalidOperationException("The container already owns its database source.");
        _source = source;
    }

    /// <summary>
    /// Clears ordinary test rows without dropping the database or its owned connection pool.
    /// Prepared seed restoration and schema recreation are separate baseline operations.
    /// </summary>
    public async Task ResetAsync()
    {
        var stage = "ResetConnect";
        try
        {
            await using var connection = await DataSource.OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await connection.ExecuteNonQueryAsync("SET LOCAL lock_timeout = '5s'");
            stage = "ResetRows";
            await using var tables = new NpgsqlCommand("""
                SELECT string_agg(format('%I.%I', n.nspname, c.relname), ', ' ORDER BY n.nspname, c.relname)
                FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE c.relkind IN ('r', 'p') AND NOT c.relispartition
                  AND n.nspname !~ '^pg_' AND n.nspname <> 'information_schema'
                """, connection, transaction);
            var names = await tables.ExecuteScalarAsync();
            if (names is string tableNames)
            {
                await using var cleanup = new NpgsqlCommand($"TRUNCATE TABLE {tableNames} RESTART IDENTITY", connection, transaction);
                await cleanup.ExecuteNonQueryAsync();
            }
            stage = "ResetCommit";
            await transaction.CommitAsync();
        }
        catch (Exception error)
        {
            throw new TtLifecycleException(stage, error);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_source is not null) await _source.DisposeAsync();
        try
        {
            await _dockerClient.Containers.StopContainerAsync(Id,
                new ContainerStopParameters { WaitBeforeKillSeconds = 5 });
            await _dockerClient.Containers.RemoveContainerAsync(Id,
                new ContainerRemoveParameters { Force = true, RemoveVolumes = true });
        }
        catch (DockerApiException error) when (error.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // A missing container is confirmed deletion, including a concurrent cleanup.
        }
    }
}
