using System.Net;
using System.Net.Sockets;
using Docker.DotNet;
using Npgsql;
using TurboTest.EFCore;
using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

public class ContainerPortTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Container_start_and_queries_succeed_when_another_process_claims_the_prechecked_port()
    {
        using var docker = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
        TcpListener? competingProcess = null;
        await using var pool = new TtPool(new TtOptions { PoolSize = 1 }, createResource: async parameters =>
        {
            // A different process can bind after a free-port probe is released.
            // If no host port is preselected, it instead occupies an unrelated port.
            var requested = parameters.HostConfig.PortBindings["5432/tcp"].Single().HostPort;
            competingProcess = new TcpListener(IPAddress.Loopback,
                string.IsNullOrEmpty(requested) ? 0 : int.Parse(requested));
            competingProcess.Start();
            return await docker.Containers.CreateContainerAsync(parameters);
        });
        try
        {
            await pool.InitializeAsync();
            var identity = Assert.Single(pool.OwnedResourceIds);
            var inspected = await docker.Containers.InspectContainerAsync(identity);
            var actualPort = int.Parse(inspected.NetworkSettings.Ports["5432/tcp"].First().HostPort);
            await using var database = await pool.GetDatabaseAsync();
            var connection = new NpgsqlConnectionStringBuilder(database.DataSource.ConnectionString);
            Assert.Equal(actualPort, connection.Port);
            Assert.NotEqual(((IPEndPoint)competingProcess!.LocalEndpoint).Port, actualPort);
            await using var session = await database.DataSource.OpenConnectionAsync();
            await using var query = new NpgsqlCommand("SELECT current_database()", session);
            Assert.Equal(connection.Database, await query.ExecuteScalarAsync());
        }
        finally { competingProcess?.Stop(); }
        await pool.DisposeAsync();
        Assert.Empty(pool.OwnedResourceIds);
        Assert.Equal(0, pool.Snapshot.Reserved);
    }
}
