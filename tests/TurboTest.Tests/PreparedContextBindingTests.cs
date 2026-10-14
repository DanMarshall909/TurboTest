using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using TurboTest.EFCore;

namespace TurboTest.Tests;

public class PreparedContextBindingTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task EnsureCreated_cannot_publish_a_runtime_model_that_disagrees_with_its_schema_model()
    {
        await using var alternative = new AlternativeContext(
            new DbContextOptionsBuilder<AlternativeContext>().UseNpgsql("Host=unused;Username=test").Options);
        var definition = new TtBaselineDefinition<ChangingContext>("mismatch-v1", TtSchemaInitialization.EnsureCreated,
            configureContext: builder => builder.UseModel(alternative.Model));
        TtFactory<ChangingContext>? published = null;
        try
        {
            var error = await Record.ExceptionAsync(async () =>
            {
                published = await Tt.CreateFactoryAsync(definition, o => o.PoolSize = 1);
            });
            Assert.Null(published);
            Assert.Equal("BaselineConfiguration", Assert.IsType<TtLifecycleException>(error).Stage);
        }
        finally { if (published is not null) await published.DisposeAsync(); }
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task The_same_context_and_initializer_version_with_different_models_have_independent_templates()
    {
        TtBaselineDefinition<ChangingContext> Definition(string email) => new("same-version",
            TtSchemaInitialization.EnsureCreated, async (context, cancellation) =>
            {
                context.Users.Add(new User { Email = email });
                await context.SaveChangesAsync(cancellation);
            }, builder => builder.ReplaceService<IModelCacheKeyFactory, FixtureModelCacheKey>());
        ChangingContext.TableForNewContexts = "Users";
        try
        {
            await using var first = await Tt.CreateFactoryAsync(Definition("first-owner@example.com"), o => o.PoolSize = 1);
            ChangingContext.TableForNewContexts = "UnpreparedUsers";
            await using var second = await Tt.CreateFactoryAsync(Definition("second-owner@example.com"), o => o.PoolSize = 1);
            await using var firstLease = await first.CreateLeaseAsync();
            await using var secondLease = await second.CreateLeaseAsync();
            Assert.Equal("first-owner@example.com", (await firstLease.Context.Users.SingleAsync()).Email);
            Assert.Equal("second-owner@example.com", (await secondLease.Context.Users.SingleAsync()).Email);
            Assert.Equal("Users", firstLease.Context.Model.FindEntityType(typeof(User))!.GetTableName());
            Assert.Equal("UnpreparedUsers", secondLease.Context.Model.FindEntityType(typeof(User))!.GetTableName());
            await first.DisposeAsync();
            Assert.Equal("second-owner@example.com", (await secondLease.Context.Users.SingleAsync()).Email);
        }
        finally { ChangingContext.TableForNewContexts = "Users"; }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [Trait("Category", "PostgreSQL")]
    public async Task Late_context_configuration_cannot_publish_an_unowned_source_or_incompatible_model(bool source, bool additional)
    {
        var definition = new TtBaselineDefinition<ChangingContext>("bound-v1", TtSchemaInitialization.EnsureCreated);
        await using var alternative = new AlternativeContext(
            new DbContextOptionsBuilder<AlternativeContext>().UseNpgsql("Host=unused;Username=test").Options);
        await using var fixture = await Tt.CreateFactoryAsync(definition, o => o.PoolSize = 1);
        TtLease<ChangingContext>? lease = null;
        try
        {
            if (additional)
            {
                lease = await fixture.CreateLeaseAsync();
                _ = lease.Context.Model;
            }
            ChangingContext.RedirectSource = source;
            ChangingContext.OverrideModel = source ? null : alternative.Model;
            ChangingContext? published = null;
            Exception? error;
            if (additional) error = Record.Exception(() => { published = lease!.CreateContext(); });
            else error = await Record.ExceptionAsync(async () =>
            {
                lease = await fixture.CreateLeaseAsync();
                published = lease.Context;
            });
            Assert.Null(published);
            Assert.Equal("ContextConfiguration", Assert.IsType<TtLifecycleException>(error).Stage);
        }
        finally
        {
            ChangingContext.RedirectSource = false;
            ChangingContext.OverrideModel = null;
            if (lease is not null) await lease.DisposeAsync();
        }
        await using var recovered = await fixture.CreateLeaseAsync();
        Assert.Empty(await recovered.Context.Users.ToListAsync());
    }

    public sealed class ChangingContext(DbContextOptions<ChangingContext> options) : DbContext(options)
    {
        public static string TableForNewContexts { get; set; } = "Users";
        public string FixtureTable { get; } = TableForNewContexts;
        public static bool RedirectSource { get; set; }
        public static IModel? OverrideModel { get; set; }
        public DbSet<User> Users => Set<User>();
        protected override void OnModelCreating(ModelBuilder builder) => builder.Entity<User>().ToTable(FixtureTable);
        protected override void OnConfiguring(DbContextOptionsBuilder builder)
        {
            if (RedirectSource) builder.UseNpgsql("Host=unowned;Database=external;Username=test");
            if (OverrideModel is not null) builder.UseModel(OverrideModel);
        }
    }

    public sealed class FixtureModelCacheKey : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) =>
            (context.GetType(), ((ChangingContext)context).FixtureTable, designTime);
    }

    public sealed class AlternativeContext(DbContextOptions<AlternativeContext> options) : DbContext(options)
    {
        public DbSet<User> Users => Set<User>();
        protected override void OnModelCreating(ModelBuilder builder) => builder.Entity<User>().ToTable("UnpreparedUsers");
    }
}
