namespace TurboTest.Tests;

internal static class PreparedTestWait
{
    // Native lifecycle tests must observe reset completion before advancing a
    // manual clock or asserting idle maintenance. Lease disposal is only handoff.
    internal static async Task UntilAsync(Func<bool> completed)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!completed()) await Task.Delay(5, deadline.Token);
    }
}
