using TurboTest.EFCore;

namespace TurboTest.Tests;

public class LifecycleDiagnosticRetentionTests
{
    [Fact]
    public void Wrapping_a_recovery_failure_retains_all_safe_diagnostics_without_private_payloads()
    {
        var original = new TtLifecycleException("Rewarm", new IOException("private-original-payload"));
        var recovery = original.WithCleanupFailure(new UnauthorizedAccessException("private-cleanup-payload"));
        var drain = new TtLifecycleException("PoolDrain", recovery);
        Assert.Contains("IOException", drain.Message);
        Assert.Contains("UnauthorizedAccessException", drain.Message);
        Assert.DoesNotContain("private-original-payload", drain.ToString());
        Assert.DoesNotContain("private-cleanup-payload", drain.ToString());
        Assert.Null(drain.InnerException);
    }
}
