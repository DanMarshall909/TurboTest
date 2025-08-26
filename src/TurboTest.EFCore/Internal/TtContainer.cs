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
    private readonly string _databaseName;
    private readonly string _templateDbName;
    private bool _templateCreated;

    public string Id { get; }
    public string ConnectionString { get; }

    public TtContainer(string id, string connectionString, DockerClient dockerClient)
    {
        Id = id;
        ConnectionString = connectionString;
        _dockerClient = dockerClient;
        
        // Extract database name from connection string
        var match = System.Text.RegularExpressions.Regex.Match(connectionString, @"Database=([^;]+)");
        _databaseName = match.Success ? match.Groups[1].Value : "testdb";
        _templateDbName = $"template_{_databaseName}";
    }

    /// <summary>
    /// Resets the database to a clean state using PostgreSQL template databases for speed.
    /// Each container has its own unique database name to avoid conflicts.
    /// </summary>
    public async Task ResetAsync()
    {
        using var connection = new NpgsqlConnection(ConnectionString.Replace($"Database={_databaseName}", "Database=postgres"));
        await connection.OpenAsync();

        if (!_templateCreated)
        {
            // Create template on first use with proper cleanup
            await connection.ExecuteNonQueryAsync($"DROP DATABASE IF EXISTS \"{_databaseName}\"");
            await connection.ExecuteNonQueryAsync($"DROP DATABASE IF EXISTS \"{_templateDbName}\"");
            await connection.ExecuteNonQueryAsync($"CREATE DATABASE \"{_databaseName}\"");

            using var testConnection = new NpgsqlConnection(ConnectionString);
            await testConnection.OpenAsync();
            // Database is ready for use
            await testConnection.CloseAsync();

            await connection.ExecuteNonQueryAsync($"CREATE DATABASE \"{_templateDbName}\" TEMPLATE \"{_databaseName}\"");
            _templateCreated = true;
        }
        else
        {
            // Fast reset using template - terminate connections first
            await connection.ExecuteNonQueryAsync($"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{_databaseName}' AND pid <> pg_backend_pid()");
            
            // Small delay to ensure connections are closed
            await Task.Delay(100);
            
            await connection.ExecuteNonQueryAsync($"DROP DATABASE IF EXISTS \"{_databaseName}\"");
            await connection.ExecuteNonQueryAsync($"CREATE DATABASE \"{_databaseName}\" TEMPLATE \"{_templateDbName}\"");
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