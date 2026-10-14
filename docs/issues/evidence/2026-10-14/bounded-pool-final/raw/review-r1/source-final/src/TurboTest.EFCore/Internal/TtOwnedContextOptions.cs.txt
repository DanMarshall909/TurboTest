using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace TurboTest.EFCore.Internal;

internal static class TtOwnedContextOptions
{
    internal static DbContextOptions<TContext> Read<TContext>(TContext context, NpgsqlDataSource source, string stage)
        where TContext : DbContext
    {
        var configuration = (DbContextOptions<TContext>)context.GetService<IDbContextOptions>();
        // Pinned-provider identity is required: endpoint equality also accepts an unowned pool.
#pragma warning disable EF1001
        var configuredSource = configuration.Extensions
            .OfType<Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal.NpgsqlOptionsExtension>()
            .SingleOrDefault()?.DataSource;
#pragma warning restore EF1001
        if (!ReferenceEquals(configuredSource, source))
            throw new TtLifecycleException(stage, new InvalidOperationException("The context must use its fixture-owned source."));
        return configuration;
    }
}
