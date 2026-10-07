using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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
            [new TextContentBlock("Привіт, світе! 🧵")],
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
        Assert.Equal(commentary.Content[0].Html, reloaded.Content[0].Html);
        Assert.Equal(commentary.CreatedAtUtc, reloaded.CreatedAtUtc);
        Assert.Equal(DateTimeKind.Utc, reloaded.CreatedAtUtc.Kind);
    }

    [Theory]
    [InlineData(nameof(Commentary.Username), Commentary.MaxUsernameLength)]
    [InlineData(nameof(Commentary.Email), Commentary.MaxEmailLength)]
    public async Task SaveChangesAsync_RejectsValuesExceedingDatabaseLimits(string propertyName, int maxLength)
    {
        await using ThreadlyDbContext context = CreateContext();
        Commentary commentary = new("Denis", "denis@example.com", [new TextContentBlock("Commentary")], DateTime.UtcNow);
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

    [Fact]
    public async Task RepliesMigration_PreservesExistingRowsAsRoots()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using ThreadlyDbContext context = CreateContext();
        await context.GetService<IMigrator>().MigrateAsync("20261004161123_InitialCommentaries", token);
        Guid id = Guid.NewGuid();
        DateTime timestamp = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc).AddTicks(7654321);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO Commentaries (Id, Username, Email, Text, CreatedAtUtc) VALUES ({id}, {"Reader"}, {"reader@example.com"}, {"Existing comment 🧵"}, {timestamp})", token);

        await context.Database.MigrateAsync(token);
        Commentary comment = await context.Commentaries.SingleAsync(token);
        Assert.Equal(id, comment.Id);
        Assert.Equal("Reader", comment.Username);
        Assert.Equal("reader@example.com", comment.Email);
        Assert.Equal("Existing comment 🧵", comment.Content[0].Html);
        Assert.Equal(timestamp, comment.CreatedAtUtc);
        Assert.Null(comment.ParentId);
    }

    [Fact]
    public async Task ContentMigrationEscapesExistingTextWithoutTruncation()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using ThreadlyDbContext context = CreateContext();
        await context.GetService<IMigrator>().MigrateAsync("20261006133517_AddCommentaryAttachments", token);
        string text = new string('&', 1980) + "<i>plain</i> 🧵";
        Guid id = Guid.NewGuid();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO Commentaries (Id, Username, Email, Text, CreatedAtUtc) VALUES ({id}, {"Reader"}, {"reader@example.com"}, {text}, {DateTime.UtcNow})", token);
        await context.Database.MigrateAsync(token);
        Commentary stored = await context.Commentaries.SingleAsync(token);
        Assert.Equal(text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;"), stored.Content[0].Html);
    }

    [Fact]
    public async Task ParentRelationship_RejectsSelfReferencesDeletionAndChangesAfterCreation()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using ThreadlyDbContext context = CreateContext();
        Commentary parent = new("Reader", "reader@example.com", [new TextContentBlock("Parent")], DateTime.UtcNow);
        Commentary reply = new("Reader", "reader@example.com", [new TextContentBlock("Reply")], DateTime.UtcNow, parent.Id);
        context.AddRange(parent, reply);
        await context.SaveChangesAsync(token);

        SqlException self = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Commentaries SET ParentId = Id WHERE Id = {parent.Id}", token));
        Assert.Equal(547, self.Number);
        Assert.Contains("CK_Commentaries_ParentId_NotSelf", self.Message);
        SqlException deletion = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM Commentaries WHERE Id = {parent.Id}", token));
        Assert.Equal(547, deletion.Number);
        Assert.Equal(2, await context.Commentaries.CountAsync(token));

        context.Entry(reply).Property(value => value.ParentId).CurrentValue = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(token));
    }

    private WebApplicationFactory<Program> GetFactory()
    {
        return factory ?? throw new InvalidOperationException("The test application has not been initialized.");
    }
}
