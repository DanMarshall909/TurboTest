using System.Collections.Concurrent;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace TurboTest.EFCore.Internal;

/// <summary>
/// Internal container pool implementation. Hidden from users.
/// </summary>
internal sealed class TtPool : IAsyncDisposable
{
    private readonly ConcurrentQueue<TtContainer> _availableContainers = new();
    private readonly ConcurrentDictionary<string, TtContainer> _activeContainers = new();
    private readonly DockerClient _dockerClient;
    private readonly SemaphoreSlim _initSemaphore = new(1, 1);
    private bool _initialized;
    private bool _disposed;
    private int _poolSize = 3;

    public TtPool()
    {
        var dockerUri = Environment.OSVersion.Platform == PlatformID.Win32NT
            ? "npipe://./pipe/docker_engine"
            : "unix:///var/run/docker.sock";
            
        _dockerClient = new DockerClientConfiguration(new Uri(dockerUri)).CreateClient();
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;

        await _initSemaphore.WaitAsync();
        try
        {
            if (_initialized) return;

            // Pull PostgreSQL image if needed
            await EnsureImageAsync("postgres:16-alpine");

            // Create initial pool
            var tasks = new List<Task<TtContainer>>();
            for (int i = 0; i < _poolSize; i++)
            {
                tasks.Add(CreateContainerAsync($"pool_{i}"));
            }

            var containers = await Task.WhenAll(tasks);
            foreach (var container in containers)
            {
                _availableContainers.Enqueue(container);
            }

            _initialized = true;
        }
        finally
        {
            _initSemaphore.Release();
        }
    }

    public async Task<TtDatabase> GetDatabaseAsync()
    {
        if (_availableContainers.TryDequeue(out var container))
        {
            _activeContainers.TryAdd(container.Id, container);
            await container.ResetAsync();
            return new TtDatabase(this, container);
        }

        // Create overflow container
        container = await CreateContainerAsync($"overflow_{Guid.NewGuid():N}");
        _activeContainers.TryAdd(container.Id, container);
        return new TtDatabase(this, container);
    }

    internal async Task ReleaseAsync(TtContainer container)
    {
        if (!_activeContainers.TryRemove(container.Id, out _))
            return;

        try
        {
            await container.ResetAsync();

            if (_availableContainers.Count < _poolSize)
            {
                _availableContainers.Enqueue(container);
            }
            else
            {
                await container.DisposeAsync();
            }
        }
        catch
        {
            await container.DisposeAsync();
        }
    }

    private async Task<TtContainer> CreateContainerAsync(string name)
    {
        var port = GetAvailablePort();
        var password = "test_" + Guid.NewGuid().ToString("N")[..8];
        
        var createResponse = await _dockerClient.Containers.CreateContainerAsync(
            new CreateContainerParameters
            {
                Image = "postgres:16-alpine",
                Name = $"tt_{name}_{DateTime.UtcNow:yyyyMMddHHmmss}",
                Env = new[]
                {
                    "POSTGRES_DB=testdb",
                    $"POSTGRES_PASSWORD={password}",
                    "POSTGRES_USER=postgres"
                },
                ExposedPorts = new Dictionary<string, EmptyStruct>
                {
                    { "5432/tcp", default }
                },
                HostConfig = new HostConfig
                {
                    PortBindings = new Dictionary<string, IList<PortBinding>>
                    {
                        ["5432/tcp"] = new[] { new PortBinding { HostPort = port.ToString() } }
                    },
                    Memory = 256 * 1024 * 1024, // 256MB
                    AutoRemove = false,
                    RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.No }
                }
            });

        await _dockerClient.Containers.StartContainerAsync(createResponse.ID, new ContainerStartParameters());
        
        var connectionString = $"Host=localhost;Port={port};Database=testdb;Username=postgres;Password={password};Include Error Detail=true";
        
        // Wait for database to be ready
        await WaitForDatabaseReadyAsync(connectionString);
        
        return new TtContainer(createResponse.ID, connectionString, _dockerClient);
    }

    private async Task WaitForDatabaseReadyAsync(string connectionString)
    {
        using var connection = new Npgsql.NpgsqlConnection(connectionString);
        var maxAttempts = 60; // 30 seconds max
        
        for (int i = 0; i < maxAttempts; i++)
        {
            try
            {
                await connection.OpenAsync();
                await connection.ExecuteScalarAsync("SELECT 1");
                return;
            }
            catch
            {
                await Task.Delay(500);
            }
        }
        
        throw new TimeoutException("Database failed to start within 30 seconds");
    }

    private async Task EnsureImageAsync(string image)
    {
        var images = await _dockerClient.Images.ListImagesAsync(new ImagesListParameters());
        if (!images.Any(i => i.RepoTags?.Contains(image) == true))
        {
            await _dockerClient.Images.CreateImageAsync(
                new ImagesCreateParameters { FromImage = image },
                null,
                new Progress<JSONMessage>());
        }
    }

    private static int GetAvailablePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        
        var allContainers = _availableContainers.ToArray()
            .Concat(_activeContainers.Values)
            .ToArray();
            
        await Task.WhenAll(allContainers.Select(c => c.DisposeAsync().AsTask()));
        
        _dockerClient?.Dispose();
        _initSemaphore?.Dispose();
        _disposed = true;
    }
}