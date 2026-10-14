namespace TurboTest.EFCore.Internal;

internal interface ITtDatabasePool : IAsyncDisposable
{
    Task<TtDatabase> GetDatabaseAsync();
}
