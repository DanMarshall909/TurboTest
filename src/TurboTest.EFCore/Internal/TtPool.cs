using System.Collections.Concurrent;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;

namespace TurboTest.EFCore.Internal;

/// <summary>
/// Internal container pool implementation. Hidden from users.
/// </summary>
internal sealed class TtPool : ITtDatabasePool
{
    private sealed class ContainerSlot(Guid id, string name)
    {
        internal Guid Id { get; } = id;
        internal string Name { get; } = name;
        internal bool CreateIssued { get; set; }
        internal bool CreationRequestSettled { get; set; }
        internal TtContainer? Container { get; set; }
        internal TtContainer Value => Container ?? throw new InvalidOperationException("The container is not prepared.");
    }

    private readonly TtOptions _options;
    private readonly ILogger _logger;
    private readonly TtSlotPool<ContainerSlot> _slots;
    private readonly ConcurrentDictionary<string, TtContainer> _ownedContainers = new();
    private readonly ConcurrentDictionary<string, ContainerSlot> _pendingCreates = new();
    private readonly DockerClient _dockerClient;
    private readonly Func<TtContainer, Task> _deleteResource;
    private readonly Func<string, CancellationToken, Task> _prepareImage;
    private readonly Func<TtContainer, Task>? _beforeStart;
    private readonly Func<CreateContainerParameters, Task<CreateContainerResponse>> _createResource;
    private readonly Func<string, CancellationToken, Task<ContainerStatsResponse?>> _readStats;
    private readonly string _image;
    private readonly bool _administratorHost;
    private readonly SemaphoreSlim _initSemaphore = new(1, 1);
    private bool _initialized;

    public IReadOnlyList<string> OwnedResourceIds => _ownedContainers.Keys.Concat(_pendingCreates.Keys).Order().ToArray();
    public TtPoolSnapshot Snapshot => _slots.Snapshot;
    public TtResourceBudget Budget { get; }

    internal async Task<TtMemoryReading?> ReadOwnedHostMemoryAsync(TimeProvider clock, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var hosts = _ownedContainers.Keys.ToArray();
        if (!_administratorHost || hosts.Length != 1) return Unavailable("HostUnavailable");
        var id = hosts[0];
        var observedAt = clock.GetTimestamp();
        ContainerStatsResponse? response;
        try
        {
            response = await _readStats(id, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return Unavailable("ProbeFailed"); }
        if (response is null || !TtHostMemory.IsForRequestedHost(response, id) || !_ownedContainers.ContainsKey(id))
            return Unavailable("HostIdentity");
        return TtHostMemory.Read(response, observedAt) ?? Unavailable("StatisticsUnavailable");
    }

    private TtMemoryReading? Unavailable(string reason)
    {
        try
        {
            _logger.LogDebug(new EventId(404, "PoolResource"),
                "Pool {OwnerId} memory unavailable: {Reason}", Snapshot.OwnerId, reason);
        }
        catch (Exception) { /* Resource observers cannot change availability. */ }
        return null;
    }

    // SDK completion follows synchronous Report and stream disposal; no callback task is orphaned.
    private sealed class StatsProgress(Action<ContainerStatsResponse> report) : IProgress<ContainerStatsResponse>
    {
        public void Report(ContainerStatsResponse value) => report(value);
    }

    private async Task<ContainerStatsResponse?> ReadStatsAsync(string id, CancellationToken cancellationToken)
    {
        ContainerStatsResponse? response = null;
        await _dockerClient.Containers.GetContainerStatsAsync(id,
            new ContainerStatsParameters { Stream = false, OneShot = true },
            new StatsProgress(value => response = value), cancellationToken);
        return response;
    }

    public TtPool(TtOptions? options = null, Func<TtContainer, Task>? deleteResource = null,
        Func<string, Task>? prepareImage = null, Func<TtContainer, Task>? beforeStart = null,
        bool administratorHost = false, Func<CreateContainerParameters, Task<CreateContainerResponse>>? createResource = null,
        Func<string, CancellationToken, Task<ContainerStatsResponse?>>? readStats = null)
    {
        _options = (options ?? new TtOptions()).Snapshot();
        _administratorHost = administratorHost;
        if (_options.EnableDynamicScaling)
            throw new ArgumentException("Adaptive warm databases require a prepared baseline factory.", nameof(TtOptions.EnableDynamicScaling));
        _deleteResource = deleteResource ?? (container => container.DisposeAsync().AsTask());
        _prepareImage = prepareImage is null ? EnsureImageAsync : (image, _) => prepareImage(image);
        _beforeStart = beforeStart;
        _image = _options.DockerImage ?? "postgres:16-alpine";
        _logger = new TtLogger(_options.Logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, _options.LogLevel);

        // Apply auto-allocation if enabled
        if (_options.AutoAllocateResources)
        {
            ApplyAutoResourceAllocation();
        }
        Budget = TtResourceBudget.Create(_options, prepared: false);

        var dockerUri = _options.DockerHost ?? (IsWindows()
            ? "npipe://./pipe/docker_engine"
            : "unix:///var/run/docker.sock");

        _dockerClient = new DockerClientConfiguration(new Uri(dockerUri)).CreateClient();
        // Once issued, creation must settle so its response can establish cleanup
        // identity. Canceling HTTP observation cannot undo Docker's side effect.
        _createResource = createResource ?? (parameters => _dockerClient.Containers.CreateContainerAsync(parameters));
        _readStats = readStats ?? ReadStatsAsync;
        _slots = new TtSlotPool<ContainerSlot>(_options.PoolSize, _options.PreparationConcurrency,
            id => new ContainerSlot(id, $"{_options.ContainerPrefix}_pool_{id:N}_{Guid.NewGuid():N}"),
            CreateContainerAsync, DeleteSlotAsync, _options.ResetConcurrency,
            logger: _options.Logger, logLevel: _options.LogLevel);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) return;

        await _initSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;

            // Pull PostgreSQL image if needed
            cancellationToken.ThrowIfCancellationRequested();
            await _prepareImage(_image, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            // Create initial pool
            _logger.LogInformation("Initializing TurboTest pool with {PoolSize} containers, {MemoryLimitMb}MB each",
                _options.PoolSize, _options.MemoryLimitMb);

            await _slots.WarmAsync(_options.PoolSize, cancellationToken);

            _logger.LogInformation("TurboTest pool initialized successfully with {ContainerCount} containers",
                _options.PoolSize);

            _initialized = true;
        }
        finally
        {
            _initSemaphore.Release();
        }
    }

    public async Task<TtDatabase> GetDatabaseAsync(CancellationToken cancellationToken = default)
    {
        var reservation = await _slots.AcquireAsync(cancellationToken);
        return new TtDatabase(reservation.Value.Value.DataSource,
            () => _slots.ReturnAsync(reservation, (slot, _) => slot.Value.ResetAsync()),
            () => _slots.DiscardAsync(reservation));
    }

    private async Task DeleteContainerAsync(TtContainer container)
    {
        await _deleteResource(container);
        _ownedContainers.TryRemove(container.Id, out _);
    }

    private async Task DeleteSlotAsync(ContainerSlot slot)
    {
        if (slot.Container is null && slot.CreateIssued)
        {
            try
            {
                var inspected = await _dockerClient.Containers.InspectContainerAsync(slot.Name);
                if (inspected.Config.Labels is null ||
                    !inspected.Config.Labels.TryGetValue("turbotest.reservation", out var reservationId) ||
                    reservationId != slot.Id.ToString("N"))
                    throw new TtLifecycleException("ContainerOwnership", new InvalidOperationException("The intended container identity is not owned."));
                slot.Container = new TtContainer(inspected.ID, _dockerClient);
                _ownedContainers.TryAdd(inspected.ID, slot.Container);
                _pendingCreates.TryRemove(slot.Name, out _);
            }
            catch (DockerApiException error) when (error.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                if (!slot.CreationRequestSettled)
                    throw new TtLifecycleException("ContainerCreateUnconfirmed", error);
                _pendingCreates.TryRemove(slot.Name, out _);
            }
        }
        if (slot.Container is not null) await DeleteContainerAsync(slot.Container);
    }

    private async Task CreateContainerAsync(ContainerSlot slot, CancellationToken cancellationToken)
    {
        // Docker owns allocation and binding atomically. A free-port probe on
        // this process cannot reserve the port for the later container start.
        const int port = 5432; // Validate the connection template before side effects.
        var password = _options.Password ?? "test_" + Guid.NewGuid().ToString("N")[..8];
        var username = _options.Username ?? "postgres";
        var databaseName = $"{_options.DatabaseName}_{Guid.NewGuid():N}"; // Make database name unique
        var image = _image;

        var connection = _options.ConnectionStringTemplate is not null
            ? new Npgsql.NpgsqlConnectionStringBuilder(string.Format(_options.ConnectionStringTemplate,
                "localhost", port, databaseName, username, password))
            : new Npgsql.NpgsqlConnectionStringBuilder
            {
                Host = "localhost", Port = port, Database = databaseName,
                Username = username, Password = password, IncludeErrorDetail = true
            };
        connection.Pooling = !_administratorHost;
        if (_administratorHost) connection.Enlist = false;
        connection.MinPoolSize = 0;
        connection.MaxPoolSize = _options.MaxConnectionsPerDatabase;
        // Validate the source before creating a container, including custom connection templates.
        await using var validation = Npgsql.NpgsqlDataSource.Create(connection.ConnectionString);
        TtContainer? container = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            slot.CreateIssued = true;
            _pendingCreates.TryAdd(slot.Name, slot);
            var createResponse = await _createResource(
                new CreateContainerParameters
                {
                    Image = image,
                    Name = slot.Name,
                    Labels = new Dictionary<string, string> { ["turbotest.reservation"] = slot.Id.ToString("N") },
                    Env = new[]
                    {
                    // These clusters are disposable. Skip initdb's initial disk flush,
                    // retaining PostgreSQL server fsync for the actual test workload.
                    "POSTGRES_INITDB_ARGS=--no-sync",
                    $"POSTGRES_DB={databaseName}",
                    $"POSTGRES_PASSWORD={password}",
                    $"POSTGRES_USER={username}"
                    },
                    ExposedPorts = new Dictionary<string, EmptyStruct>
                    {
                    { "5432/tcp", default }
                    },
                    HostConfig = new HostConfig
                    {
                        PortBindings = new Dictionary<string, IList<PortBinding>>
                        {
                            ["5432/tcp"] = new[] { new PortBinding { HostPort = "" } }
                        },
                        Memory = (long)_options.MemoryLimitMb * 1024 * 1024,
                        AutoRemove = false,
                        RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.No }
                    }
                });
            slot.CreationRequestSettled = true;

            // Retain ownership before any operation which can fail or await readiness.
            container = new TtContainer(createResponse.ID, _dockerClient);
            slot.Container = container;
            _ownedContainers.TryAdd(container.Id, container);
            _pendingCreates.TryRemove(slot.Name, out _);
            if (_beforeStart is not null) await _beforeStart(container);
            cancellationToken.ThrowIfCancellationRequested();
            await _dockerClient.Containers.StartContainerAsync(createResponse.ID, new ContainerStartParameters(), cancellationToken);
            var inspected = await _dockerClient.Containers.InspectContainerAsync(createResponse.ID, cancellationToken);
            var published = inspected.NetworkSettings.Ports["5432/tcp"].First().HostPort;
            connection.Port = int.Parse(published, System.Globalization.CultureInfo.InvariantCulture);
            var dataSource = Npgsql.NpgsqlDataSource.Create(connection.ConnectionString);
            container.AttachDataSource(dataSource);
            await WaitForDatabaseReadyAsync(dataSource, cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            if (error is DockerApiException) slot.CreationRequestSettled = true;
            throw new TtLifecycleException("ContainerPrepare", error);
        }
    }

    internal static async Task WaitForDatabaseReadyAsync(Npgsql.NpgsqlDataSource dataSource,
        Func<Npgsql.NpgsqlConnection, Task>? probe = null, int maxAttempts = 60,
        CancellationToken cancellationToken = default)
    {
        for (int i = 0; i < maxAttempts; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var connection = dataSource.CreateConnection();
                await connection.OpenAsync(cancellationToken);
                if (probe is not null) await probe(connection);
                else
                {
                    await using var query = new Npgsql.NpgsqlCommand("SELECT 1", connection);
                    await query.ExecuteScalarAsync(cancellationToken);
                }
                cancellationToken.ThrowIfCancellationRequested();
                return;
            }
            catch
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(500, cancellationToken);
            }
        }

        throw new TimeoutException("Database failed to start within 30 seconds");
    }

    private async Task EnsureImageAsync(string image, CancellationToken cancellationToken)
    {
        var images = await _dockerClient.Images.ListImagesAsync(new ImagesListParameters(), cancellationToken);
        if (!images.Any(i => i.RepoTags?.Contains(image) == true))
        {
            await _dockerClient.Images.CreateImageAsync(
                new ImagesCreateParameters { FromImage = image },
                null,
                new Progress<JSONMessage>(), cancellationToken);
        }
    }

    public void StopAcceptingWork() => _slots.StopAcceptingWork();

    public async ValueTask DisposeAsync()
    {
        await _slots.RetryCleanupAsync();
        _dockerClient?.Dispose();
        _initSemaphore?.Dispose();
    }

    private void ApplyAutoResourceAllocation()
    {
        var systemInfo = GetSystemInfo();

        _logger.LogDebug("Auto-allocation enabled. System info: {CpuCores} CPU cores, {TotalMemoryMb}MB total memory",
            systemInfo.CpuCores, systemInfo.TotalMemoryMb);

        // Calculate available memory after reserving requested amount
        var availableMemoryMb = Math.Max(0, systemInfo.TotalMemoryMb - _options.ReserveMemoryMb);

        // Calculate available CPU cores after reserving requested amount  
        var availableCores = Math.Max(1, systemInfo.CpuCores - _options.ReserveCpuCores);

        _logger.LogDebug("After reserves: {AvailableMemoryMb}MB memory ({ReservedMb}MB reserved), {AvailableCores} CPU cores ({ReservedCores} reserved)",
            availableMemoryMb, _options.ReserveMemoryMb, availableCores, _options.ReserveCpuCores);

        // Calculate optimal container count based on available resources
        // Each container should get reasonable memory allocation (minimum 128MB, optimal ~512MB)
        var containersByMemory = availableMemoryMb / 512; // Prefer 512MB per container
        var containersByCpu = availableCores; // One container per available core

        _logger.LogDebug("Container calculations: {ContainersByMemory} by memory (512MB each), {ContainersByCpu} by CPU",
            containersByMemory, containersByCpu);

        // Use the more conservative limit
        var optimalContainers = Math.Min(containersByMemory, containersByCpu);

        // Apply bounds
        optimalContainers = Math.Max(1, Math.Min(optimalContainers,
            Math.Min(_options.MaxAutoContainers, _options.MaxContainers ?? int.MaxValue)));

        // Calculate memory per container
        var memoryPerContainer = optimalContainers > 0 ? availableMemoryMb / optimalContainers : 256;
        memoryPerContainer = Math.Max(128, Math.Min(memoryPerContainer, 2048)); // Between 128MB-2GB

        _logger.LogInformation("Auto-allocated resources: {ContainerCount} containers, {MemoryPerContainer}MB each (was: {OriginalPoolSize} containers, {OriginalMemory}MB each)",
            optimalContainers, memoryPerContainer, _options.PoolSize, _options.MemoryLimitMb);

        // Update options
        _options.PoolSize = optimalContainers;
        _options.MemoryLimitMb = memoryPerContainer;
    }

    private SystemInfo GetSystemInfo() =>
        TryGetValue(() => new SystemInfo(Environment.ProcessorCount, GetTotalMemoryMb()), new SystemInfo(4, 8192));

    private int GetTotalMemoryMb() => TryGetValue(() => IsWindows() ? GetWindowsMemoryMb() : GetLinuxMemoryMb(), 8192);

    private int GetWindowsMemoryMb()
    {
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "wmic",
                Arguments = "computersystem get TotalPhysicalMemory /value",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            }
        };

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        var match = System.Text.RegularExpressions.Regex.Match(output, @"TotalPhysicalMemory=(\d+)");
        if (match.Success && long.TryParse(match.Groups[1].Value, out var bytes))
        {
            return (int)(bytes / 1024 / 1024); // Convert to MB
        }

        return 8192; // Fallback
    }

    private int GetLinuxMemoryMb()
    {
        var meminfo = File.ReadAllText("/proc/meminfo");
        var match = System.Text.RegularExpressions.Regex.Match(meminfo, @"MemTotal:\s*(\d+)\s*kB");

        if (match.Success && int.TryParse(match.Groups[1].Value, out var kb))
        {
            return kb / 1024; // Convert to MB
        }

        return 8192; // Fallback
    }

    private static bool IsWindows() => Environment.OSVersion.Platform == PlatformID.Win32NT;

    private static T TryGetValue<T>(Func<T> getter, T fallback)
    {
        try { return getter(); }
        catch { return fallback; }
    }

    private record SystemInfo(int CpuCores, int TotalMemoryMb);
}
