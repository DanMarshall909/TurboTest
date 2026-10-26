using TurboTest.EFCore;

namespace TurboTest.Tests;

public class LifecycleDiagnosticRetentionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Wrapping_a_recovery_failure_retains_all_safe_diagnostics_without_private_payloads(bool safeSecondary)
    {
        var original = new TtLifecycleException("Rewarm", new IOException("private-original-payload"));
        Exception secondary = new UnauthorizedAccessException("private-cleanup-payload");
        if (safeSecondary) secondary = new TtLifecycleException("DropOwned", secondary);
        var recovery = original.WithCleanupFailure(secondary);
        var drain = new TtLifecycleException("PoolDrain", recovery);
        Assert.Equal("PoolDrain", drain.Stage);
        Assert.Contains("Rewarm", drain.Message);
        Assert.Contains(safeSecondary ? "DropOwned" : "Discard", drain.Message);
        if (safeSecondary) Assert.DoesNotContain("Discard", drain.Message);
        Assert.Contains("IOException", drain.Message);
        Assert.Contains("UnauthorizedAccessException", drain.Message);
        Assert.DoesNotContain("private-original-payload", drain.ToString());
        Assert.DoesNotContain("private-cleanup-payload", drain.ToString());
        Assert.Null(drain.InnerException);
    }
}
