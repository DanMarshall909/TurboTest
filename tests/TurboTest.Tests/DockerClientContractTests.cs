using System.Net;
using System.Net.Sockets;
using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;
using TurboTest.EFCore;
using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

public class DockerClientContractTests
{
    [Theory]
    [InlineData(404, true)]
    [InlineData(500, false)]
    public async Task Container_deletion_accepts_confirmed_absence_but_reports_server_failure(int status, bool absent)
    {
        await using var endpoint = new ScriptedDockerEndpoint(status);
        using var docker = new DockerClientConfiguration(endpoint.Uri).CreateClient();
        var resource = new TtContainer("owned-id", docker);

        if (absent) await resource.DisposeAsync();
        else
        {
            var failure = await Assert.ThrowsAsync<DockerApiException>(() => resource.DisposeAsync().AsTask());
            Assert.Equal(HttpStatusCode.InternalServerError, failure.StatusCode);
        }

        Assert.Contains("/containers/owned-id/stop", await endpoint.Request.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Docker_client_cancels_a_stats_request_while_the_response_is_held()
    {
        await using var endpoint = new ScriptedDockerEndpoint(200, holdResponse: true);
        using var docker = new DockerClientConfiguration(endpoint.Uri).CreateClient();
        using var cancellation = new CancellationTokenSource();
        var read = docker.Containers.GetContainerStatsAsync("owned-id",
            new ContainerStatsParameters { Stream = false, OneShot = true },
            new Capture(), cancellation.Token);

        Assert.Contains("/containers/owned-id/stats", await endpoint.Request.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(read.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Cold_image_pull_requests_the_exact_configured_image()
    {
        await using var endpoint = new ScriptedDockerEndpoint(200, 500);
        await using var pool = new TtPool(new TtOptions
        {
            PoolSize = 1,
            DockerHost = endpoint.Uri.ToString(),
            DockerImage = "example.invalid/fixture-image:chosen"
        });

        var failure = await Assert.ThrowsAsync<DockerApiException>(() => pool.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(HttpStatusCode.InternalServerError, failure.StatusCode);
        Assert.Contains("/images/json", await endpoint.Request);
        var pull = await endpoint.RequestAt(1).WaitAsync(TimeSpan.FromSeconds(5));
        var target = pull.Split(' ')[1];
        var uri = new Uri(endpoint.Uri, target);
        Assert.EndsWith("/images/create", uri.AbsolutePath);
        var imageParameter = Assert.Single(uri.Query.TrimStart('?').Split('&')
            .Select(field => field.Split('=', 2)), field => field[0] == "fromImage");
        Assert.Equal("example.invalid/fixture-image:chosen", Uri.UnescapeDataString(imageParameter[1]));
        Assert.Empty(pool.OwnedResourceIds);
    }

    private sealed class Capture : IProgress<ContainerStatsResponse>
    {
        public void Report(ContainerStatsResponse value) => throw new InvalidOperationException("No stats response was sent.");
    }

    private sealed class ScriptedDockerEndpoint : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Task _server;
        private readonly TaskCompletionSource<string>[] _requests;

        internal Uri Uri { get; }
        internal Task<string> Request => _requests[0].Task;
        internal Task<string> RequestAt(int index) => _requests[index].Task;

        internal ScriptedDockerEndpoint(int status, bool holdResponse = false)
            : this([status], holdResponse) { }

        internal ScriptedDockerEndpoint(params int[] statuses)
            : this(statuses, false) { }

        private ScriptedDockerEndpoint(int[] statuses, bool holdResponse)
        {
            _listener.Start();
            Uri = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}");
            if (!holdResponse) _release.TrySetResult();
            _requests = statuses.Select(_ => new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
            _server = ServeAsync(statuses);
        }

        private async Task ServeAsync(int[] statuses)
        {
            for (var i = 0; i < statuses.Length; i++)
            {
                using var client = await _listener.AcceptTcpClientAsync();
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                var requestLine = await reader.ReadLineAsync();
                if (requestLine is null) throw new IOException("Docker client did not send a request line.");
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }
                _requests[i].TrySetResult(requestLine);
                await _release.Task;
                var status = statuses[i];
                var body = Encoding.UTF8.GetBytes(status == 200 ? "[]" : "{\"message\":\"Docker request failed\"}");
                var reason = status switch { 200 => "OK", 404 => "Not Found", _ => "Internal Server Error" };
                var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {reason}\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                try
                {
                    await stream.WriteAsync(head);
                    await stream.WriteAsync(body);
                    await stream.FlushAsync();
                }
                catch (IOException) { /* A canceled client may close before the held response is released. */ }
            }
        }

        public async ValueTask DisposeAsync()
        {
            _release.TrySetResult();
            _listener.Stop();
            try { await _server.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (SocketException) { /* Listener stopped before a later scripted request. */ }
        }
    }
}
