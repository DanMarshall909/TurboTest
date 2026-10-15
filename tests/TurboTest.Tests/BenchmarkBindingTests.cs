using System.Diagnostics;
using TurboTest.Benchmarks;

namespace TurboTest.Tests;

public class BenchmarkBindingTests
{
    [Fact]
    public async Task Binding_preserves_actual_source_and_rejects_changed_bytes_even_with_the_same_dirty_status()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tt-binding-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "src"));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, ".gitignore"), "evidence/\n");
            await File.WriteAllTextAsync(Path.Combine(directory, "src", "Model.cs"), "class First {}\n");
            await File.WriteAllTextAsync(Path.Combine(directory, "TurboTest.sln"), "fixture\n");
            await Git("init", "-q"); await Git("add", ".");
            await Git("-c", "user.name=Benchmark Test", "-c", "user.email=benchmark@example.test", "commit", "-qm", "Fixture");
            await File.WriteAllTextAsync(Path.Combine(directory, "src", "Model.cs"), "class Second {}\n");
            var before = await SourceBinding.CaptureAsync(directory, Path.Combine(directory, "evidence", "before"));
            Assert.Equal(40, before.Head.Length);
            Assert.Contains("src/Model.cs", before.DirtyState);
            var model = Assert.Single(before.Files, file => file.Path == "src/Model.cs");
            Assert.Equal("class Second {}\n", await File.ReadAllTextAsync(model.Snapshot));
            Assert.Equal(64, model.Sha256.Length);
            Assert.NotEmpty(before.AssemblyHashes);
            Assert.NotEmpty(before.Runtime);
            var same = await SourceBinding.CaptureAsync(directory, Path.Combine(directory, "evidence", "same"));
            before.RequireUnchanged(same);
            await File.WriteAllTextAsync(Path.Combine(directory, "src", "Model.cs"), "class Third {}\n");
            var after = await SourceBinding.CaptureAsync(directory, Path.Combine(directory, "evidence", "after"));
            Assert.Equal(before.DirtyState, after.DirtyState);
            var error = Assert.Throws<InvalidDataException>(() => before.RequireUnchanged(after));
            Assert.Contains("SourceChanged", error.Message);
            Assert.Equal("class Second {}\n", await File.ReadAllTextAsync(model.Snapshot));
        }
        finally { Directory.Delete(directory, recursive: true); }

        async Task Git(params string[] arguments)
        {
            var info = new ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in arguments) info.ArgumentList.Add(argument);
            using var process = Process.Start(info)!;
            await Task.WhenAll(process.StandardOutput.ReadToEndAsync(), process.StandardError.ReadToEndAsync());
            await process.WaitForExitAsync();
            Assert.Equal(0, process.ExitCode);
        }
    }
}
