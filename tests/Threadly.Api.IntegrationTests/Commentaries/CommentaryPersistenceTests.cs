using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Threadly.Api.IntegrationTests.Fixtures;
using Threadly.Domain.Commentaries;
using Threadly.Infrastructure;
using Threadly.Infrastructure.Persistence;
using Xunit;

namespace Threadly.Api.IntegrationTests.Commentaries;

public sealed class CommentaryPersistenceTests(SqlServerFixture sqlServer) : IAsyncLifetime
{
    private readonly string connectionString = new SqlConnectionStringBuilder(sqlServer.ConnectionString)
    {
        InitialCatalog = $"ThreadlyTests_{Guid.NewGuid():N}"
    }.ConnectionString;

    private WebApplicationFactory<Program>? factory;

    public async ValueTask InitializeAsync()
    {
        await using ThreadlyDbContext context = CreateContext();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"ConnectionStrings:{DependencyInjection.DatabaseConnectionName}"] = connectionString
                }));
        });
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (factory is not null)
            {
                await factory.DisposeAsync();
            }
        }
        finally
        {
            await using ThreadlyDbContext context = CreateContext();
            await context.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task NewDatabase_HasAppliedMigrationsAndNoCommentaries()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ThreadlyDbContext context = CreateContext();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync(cancellationToken));
        Assert.NotEmpty(await context.Database.GetAppliedMigrationsAsync(cancellationToken));
        Assert.Empty(await context.Commentaries.ToListAsync(cancellationToken));
    }

    [Fact]
    public async Task SaveChangesAsync_ReloadsCommentaryWithOriginalIdUnicodeAndUtcTimestamp()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Commentary commentary = new(
            "Denis",
            "denis@example.com",
            "Привіт, світе! 🧵",
            new DateTime(2026, 10, 4, 12, 34, 56, DateTimeKind.Utc).AddTicks(1234567));

        await using (AsyncServiceScope scope = GetFactory().Services.CreateAsyncScope())
        {
            ThreadlyDbContext context = scope.ServiceProvider.GetRequiredService<ThreadlyDbContext>();
            context.Commentaries.Add(commentary);
            await context.SaveChangesAsync(cancellationToken);
        }

        await using AsyncServiceScope readScope = GetFactory().Services.CreateAsyncScope();
        ThreadlyDbContext readContext = readScope.ServiceProvider.GetRequiredService<ThreadlyDbContext>();
        Commentary? reloaded = await readContext.Commentaries.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == commentary.Id, cancellationToken);

        Assert.NotNull(reloaded);
        Assert.NotSame(commentary, reloaded);
        Assert.Equal(commentary.Id, reloaded.Id);
        Assert.Equal(commentary.Username, reloaded.Username);
        Assert.Equal(commentary.Email, reloaded.Email);
        Assert.Equal(commentary.Text, reloaded.Text);
        Assert.Equal(commentary.CreatedAtUtc, reloaded.CreatedAtUtc);
        Assert.Equal(DateTimeKind.Utc, reloaded.CreatedAtUtc.Kind);
    }

    [Theory]
    [InlineData(nameof(Commentary.Username), Commentary.MaxUsernameLength)]
    [InlineData(nameof(Commentary.Email), Commentary.MaxEmailLength)]
    [InlineData(nameof(Commentary.Text), Commentary.MaxTextLength)]
    public async Task SaveChangesAsync_RejectsValuesExceedingDatabaseLimits(string propertyName, int maxLength)
    {
        await using ThreadlyDbContext context = CreateContext();
        Commentary commentary = new("Denis", "denis@example.com", "Commentary", DateTime.UtcNow);
        context.Commentaries.Add(commentary);
        context.Entry(commentary).Property<string>(propertyName).CurrentValue = new string('x', maxLength + 1);

        DbUpdateException exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync(TestContext.Current.CancellationToken));

        SqlException sqlException = Assert.IsType<SqlException>(exception.InnerException);
        Assert.Contains(sqlException.Number, new[] { 8152, 2628 });
    }

    private ThreadlyDbContext CreateContext()
    {
        DbContextOptions<ThreadlyDbContext> options = new DbContextOptionsBuilder<ThreadlyDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new ThreadlyDbContext(options);
    }

    private WebApplicationFactory<Program> GetFactory()
    {
        return factory ?? throw new InvalidOperationException("The test application has not been initialized.");
    }
}
