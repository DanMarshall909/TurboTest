namespace TurboTest.Benchmarks;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler stop = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += stop;
        try { return await BenchmarkCommand.RunAsync(args, cancellation.Token); }
        finally { Console.CancelKeyPress -= stop; }
    }
}
