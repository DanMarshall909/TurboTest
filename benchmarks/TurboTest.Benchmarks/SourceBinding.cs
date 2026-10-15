using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TurboTest.EFCore;

namespace TurboTest.Benchmarks;

public sealed record BoundSource(string Path, string Sha256, string Snapshot);
public sealed record SourceBinding(string Head, string DirtyState, string Sdk, string Runtime,
    string Os, string Architecture, int LogicalProcessors, BoundSource[] Files, Dictionary<string, string> AssemblyHashes)
{
    public static async Task<SourceBinding> CaptureAsync(string repository, string directory, CancellationToken token = default)
    {
        var head = (await CommandAsync("git", repository, ["rev-parse", "HEAD"], token)).Trim();
        var dirty = await CommandAsync("git", repository, ["status", "--porcelain"], token);
        var sdk = (await CommandAsync("dotnet", repository, ["--version"], token)).Trim();
        var paths = new List<string>();
        foreach (var root in new[] { "src", "tests", "examples", "benchmarks" })
        {
            var path = Path.Combine(repository, root);
            if (!Directory.Exists(path)) continue;
            paths.AddRange(Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Where(file => Path.GetExtension(file) is ".cs" or ".csproj" &&
                    !Path.GetRelativePath(repository, file).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj")));
        }
        foreach (var file in new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "global.json", "TurboTest.sln", ".config/dotnet-tools.json", "tests/TurboTest.Tests/xunit.runner.json" })
            if (File.Exists(Path.Combine(repository, file))) paths.Add(Path.Combine(repository, file));
        var workflow = Path.Combine(repository, ".github", "workflows");
        if (Directory.Exists(workflow)) paths.AddRange(Directory.EnumerateFiles(workflow, "*.yml"));
        if (!paths.Any(path => Path.GetExtension(path) == ".cs")) throw new InvalidDataException("SourceBindingEmpty: no maintained source was found.");
        List<BoundSource> files = [];
        foreach (var path in paths.Distinct().Order(StringComparer.Ordinal))
        {
            var bytes = await File.ReadAllBytesAsync(path, token);
            var relative = Path.GetRelativePath(repository, path).Replace(Path.DirectorySeparatorChar, '/');
            var copy = Path.Combine(directory, "source", relative + ".txt");
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            await File.WriteAllBytesAsync(copy, bytes, token);
            files.Add(new(relative, Convert.ToHexString(SHA256.HashData(bytes)), Path.GetFullPath(copy)));
        }
        var assemblies = new[] { typeof(BenchmarkRunner).Assembly, typeof(Tt).Assembly, typeof(DbContext).Assembly, typeof(NpgsqlConnection).Assembly, typeof(Docker.DotNet.DockerClient).Assembly }
            .ToDictionary(assembly => assembly.GetName().Name! + "@" + assembly.GetName().Version,
                assembly => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))));
        var binding = new SourceBinding(head, dirty, sdk, RuntimeInformation.FrameworkDescription, RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(), Environment.ProcessorCount, files.ToArray(), assemblies);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "binding.json"), JsonSerializer.Serialize(binding, new JsonSerializerOptions { WriteIndented = true }), token);
        return binding;
    }

    public void RequireUnchanged(SourceBinding after)
    {
        if (Head != after.Head || DirtyState != after.DirtyState || Sdk != after.Sdk || Runtime != after.Runtime ||
            !Files.Select(file => (file.Path, file.Sha256)).SequenceEqual(after.Files.Select(file => (file.Path, file.Sha256))) ||
            !AssemblyHashes.OrderBy(pair => pair.Key).SequenceEqual(after.AssemblyHashes.OrderBy(pair => pair.Key)))
            throw new InvalidDataException("SourceChanged: source, references, compiler or loaded assemblies changed during the experiment.");
    }

    internal static async Task<string> CommandAsync(string executable, string cwd, string[] arguments, CancellationToken token)
    {
        var info = new ProcessStartInfo(executable) { WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidDataException("BindingCommand: process could not start.");
        var output = process.StandardOutput.ReadToEndAsync(token);
        var error = process.StandardError.ReadToEndAsync(token);
        try { await process.WaitForExitAsync(token); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        await error;
        if (process.ExitCode != 0) throw new InvalidDataException("BindingCommand: metadata command did not succeed.");
        return await output;
    }
}
