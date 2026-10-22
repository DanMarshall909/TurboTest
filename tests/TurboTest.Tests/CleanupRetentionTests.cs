using System.Runtime.CompilerServices;
using TurboTest.EFCore;
using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

[Collection("Static lifecycle")]
public class CleanupRetentionTests
{
    [Fact]
    public async Task Repeated_successful_recovery_releases_completed_handles_from_the_live_registry()
    {
        var handles = await Task.Run(() => Enumerable.Range(0, 16).Select(_ => CompleteRegisteredOwner()).ToArray());
        CollectCompletedObjects();
        Assert.All(handles, handle => Assert.False(handle.IsAlive));
    }

    [Fact]
    public async Task Completed_capability_releases_callback_and_resource_payloads_while_the_handle_is_still_held()
    {
        var completed = await Task.Run(CompleteOwnerWithPayloads);
        Assert.Empty(completed.Owner.OwnedResourceIds);
        CollectCompletedObjects();
        Assert.False(completed.Callback.IsAlive);
        Assert.False(completed.Resources.IsAlive);
        GC.KeepAlive(completed.Owner);
    }

    [Fact]
    public async Task Completed_composition_releases_child_handles_while_the_parent_capability_is_still_held()
    {
        var completed = await Task.Run(CompleteComposition);
        Assert.Empty(completed.Owner.OwnedResourceIds);
        CollectCompletedObjects();
        Assert.All(completed.Children, child => Assert.False(child.IsAlive));
        GC.KeepAlive(completed.Owner);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CompleteRegisteredOwner()
    {
        var completed = CompleteOwnerWithPayloads();
        return new WeakReference(completed.Owner);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (TtCleanup Owner, WeakReference Callback, WeakReference Resources) CompleteOwnerWithPayloads()
    {
        var callback = new Payload();
        var resources = new Payload();
        var owner = TtCleanupRegistry.Retain(Guid.NewGuid(), callback.CompleteAsync, resources.ReadResources);
        owner.RetryCleanupAsync().GetAwaiter().GetResult();
        return (owner, new WeakReference(callback), new WeakReference(resources));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (TtCleanup Owner, WeakReference[] Children) CompleteComposition()
    {
        var children = Enumerable.Range(0, 2).Select(_ => new TtCleanup(Guid.NewGuid(),
            () => Task.CompletedTask, () => [])).ToArray();
        var error = new TtLifecycleException("First", new IOException(),
            new TtLifecycleException("Second", new IOException(), cleanup: children[1]), children[0]);
        var owner = Assert.IsType<TtCleanup>(error.Cleanup);
        owner.RetryCleanupAsync().GetAwaiter().GetResult();
        return (owner, children.Select(child => new WeakReference(child)).ToArray());
    }

    private static void CollectCompletedObjects()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private sealed class Payload
    {
        internal Task CompleteAsync() => Task.CompletedTask;
        internal IReadOnlyList<string> ReadResources() => [];
    }
}
