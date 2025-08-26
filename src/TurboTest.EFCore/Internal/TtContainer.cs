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
    private readonly string _templateDbName = "template_tt";
    private bool _templateCreated;
    
    public string Id { get; }
    public string ConnectionString { get; }

    public TtContainer(string id, string connectionString, DockerClient dockerClient)
    {
        Id = id;
        ConnectionString = connectionString;
        _dockerClient = dockerClient;
    }

    /// <summary>
    /// Resets the database to a clean state using PostgreSQL template databases for speed.
    /// </summary>
    public async Task ResetAsync()
    {
        using var connection = new NpgsqlConnection(ConnectionString.Replace("Database=testdb", "Database=postgres"));
        await connection.OpenAsync();

        if (!_templateCreated)
        {
            // Create template on first use
            await connection.ExecuteNonQueryAsync("DROP DATABASE IF EXISTS testdb");
            await connection.ExecuteNonQueryAsync("CREATE DATABASE testdb");
            
            using var testConnection = new NpgsqlConnection(ConnectionString);
            await testConnection.OpenAsync();
            // Any initialization SQL would go here
            await testConnection.CloseAsync();
            
            await connection.ExecuteNonQueryAsync($"CREATE DATABASE {_templateDbName} TEMPLATE testdb");
            _templateCreated = true;
        }
        else
        {
            // Fast reset using template
            await connection.ExecuteNonQueryAsync("SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = 'testdb' AND pid <> pg_backend_pid()");
            await connection.ExecuteNonQueryAsync("DROP DATABASE IF EXISTS testdb");
            await connection.ExecuteNonQueryAsync($"CREATE DATABASE testdb TEMPLATE {_templateDbName}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _dockerClient.Containers.StopContainerAsync(Id, 
                new ContainerStopParameters { WaitBeforeKillSeconds = 5 });
            await _dockerClient.Containers.RemoveContainerAsync(Id, 
                new ContainerRemoveParameters { Force = true });
        }
        catch
        {
            // Best effort cleanup
        }
    }
}