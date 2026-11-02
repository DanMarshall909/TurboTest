namespace TurboTest.EFCore;

/// <summary>How a prepared fixture handles a background database reset failure.</summary>
public enum TtResetFailurePolicy
{
    /// <summary>Reject queued and later acquisitions with the reset failure; also report it at shutdown.</summary>
    StopFixture,
    /// <summary>Attempt bounded recovery and retain every reset failure for reporting at shutdown.</summary>
    RecoverAndReport
}
