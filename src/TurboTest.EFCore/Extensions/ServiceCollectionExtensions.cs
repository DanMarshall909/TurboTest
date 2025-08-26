using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace TurboTest.EFCore.Extensions;

/// <summary>
/// Extension methods for IServiceCollection to register TurboTest services
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds TurboTest services to the DI container.
    /// Uses familiar ASP.NET Core patterns for configuration.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">Configuration instance (optional)</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddTurboTest(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        // Configure options from configuration section if provided
        if (configuration != null)
        {
            services.Configure<TtOptions>(configuration.GetSection(TtOptions.SectionName));
        }
        else
        {
            services.Configure<TtOptions>(_ => { });
        }

        // Register the service factory
        services.AddSingleton<ITtServiceFactory, TtServiceFactory>();

        return services;
    }

    /// <summary>
    /// Adds TurboTest services with explicit configuration.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configure">Configuration action</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddTurboTest(
        this IServiceCollection services,
        Action<TtOptions> configure)
    {
        services.Configure(configure);
        services.AddSingleton<ITtServiceFactory, TtServiceFactory>();
        return services;
    }

    /// <summary>
    /// Registers a DbContext to use TurboTest for testing.
    /// Similar to AddDbContext but uses containerized databases.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type</typeparam>
    /// <param name="services">The service collection</param>
    /// <param name="configure">Optional context configuration</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddTurboTestDbContext<TContext>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder>? configure = null)
        where TContext : DbContext
    {
        // Ensure TurboTest is registered
        if (!services.Any(s => s.ServiceType == typeof(ITtServiceFactory)))
        {
            services.AddTurboTest();
        }

        services.AddDbContext<TContext>((serviceProvider, options) =>
        {
            var factory = serviceProvider.GetRequiredService<ITtServiceFactory>();
            var connectionString = factory.GetConnectionString();

            options.UseNpgsql(connectionString);
            configure?.Invoke(options);
        });

        return services;
    }
}