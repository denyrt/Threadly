using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Threadly.Api.IntegrationTests.Fixtures;
using Threadly.Domain.Commentaries;
using Threadly.Infrastructure;
using Threadly.Infrastructure.Persistence;
using Threadly.MigrationWorker;
using Xunit;

namespace Threadly.Api.IntegrationTests.Migrations;

public sealed class MigrationWorkerTests(SqlServerFixture sqlServer) : IAsyncLifetime
{
    private readonly string connectionString = new SqlConnectionStringBuilder(sqlServer.ConnectionString)
    {
        InitialCatalog = $"ThreadlyMigrationTests_{Guid.NewGuid():N}"
    }.ConnectionString;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await using ThreadlyDbContext db = CreateContext();
        await db.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task Executable_CreatesSchemaAndPreservesDataOnRepeat()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        Assert.Equal(0, await RunWorkerAsync(connectionString));

        Commentary commentary = new(
            "Denis",
            "denis@example.com",
            "Saved data",
            DateTime.UtcNow);

        await using (ThreadlyDbContext dbContext = CreateContext())
        {
            Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync(cancellationToken));
            Assert.Equal(dbContext.Database.GetMigrations(), await dbContext.Database.GetAppliedMigrationsAsync(cancellationToken));
            Assert.Empty(await dbContext.Commentaries.ToListAsync(cancellationToken));

            dbContext.Commentaries.Add(commentary);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        Assert.Equal(0, await RunWorkerAsync(connectionString));

        await using ThreadlyDbContext readContext = CreateContext();
        Commentary reloaded = await readContext.Commentaries.SingleAsync(cancellationToken);

        Assert.Equal(commentary.Id, reloaded.Id);
        Assert.Equal(commentary.Text, reloaded.Text);
        Assert.Equal(commentary.CreatedAtUtc, reloaded.CreatedAtUtc);
    }

    [Fact]
    public async Task Executable_ReturnsFailureWhenMigrationCannotApply()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ThreadlyDbContext dbContext = CreateContext();
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);

        Assert.NotEqual(0, await RunWorkerAsync(connectionString));

        Assert.Empty(await dbContext.Database.GetAppliedMigrationsAsync(cancellationToken));
        Assert.Equal(dbContext.Database.GetMigrations(), await dbContext.Database.GetPendingMigrationsAsync(cancellationToken));
    }

    [Fact]
    public async Task Executable_ReturnsFailureWhenMigrationTimesOut()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ThreadlyDbContext dbContext = CreateContext();
        await dbContext.Database.MigrateAsync(cancellationToken);

        await using SqlConnection migrationLock = new(connectionString);
        await AcquireMigrationLockAsync(migrationLock, cancellationToken);

        Assert.NotEqual(0, await RunWorkerAsync(connectionString, "--Migration:Timeout=00:00:01"));
    }

    [Fact]
    public async Task Executable_ReturnsFailureWhenConnectionStringIsMissing()
    {
        Assert.NotEqual(0, await RunWorkerAsync(null));
    }

    [Fact]
    public async Task ShutdownBeforeCompletion_RemainsFailure()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ThreadlyDbContext dbContext = CreateContext();
        await dbContext.Database.MigrateAsync(cancellationToken);

        await using SqlConnection migrationLock = new(connectionString);
        await AcquireMigrationLockAsync(migrationLock, cancellationToken);

        using IHost host = CreateMigrationHost();
        MigrationService migrationService = host.Services.GetRequiredService<MigrationService>();

        Assert.Equal(1, migrationService.ExitCode);

        await host.StartAsync(cancellationToken);
        await host.StopAsync(cancellationToken);

        Assert.Equal(1, migrationService.ExitCode);
    }

    private ThreadlyDbContext CreateContext()
    {
        DbContextOptions<ThreadlyDbContext> options = new DbContextOptionsBuilder<ThreadlyDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new ThreadlyDbContext(options);
    }

    private static async Task AcquireMigrationLockAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        // EF Core holds this session lock while applying SQL Server migrations.
        await connection.OpenAsync(cancellationToken);

        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = """
            EXEC sp_getapplock
                @Resource = N'__EFMigrationsLock',
                @LockOwner = N'Session',
                @LockMode = N'Exclusive';
            """;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private IHost CreateMigrationHost()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"ConnectionStrings:{DependencyInjection.DatabaseConnectionName}"] = connectionString
        });

        builder.Logging.ClearProviders();
        builder.Services.AddInfrastructure();
        builder.Services.AddSingleton<MigrationService>();
        builder.Services.AddHostedService(services => services.GetRequiredService<MigrationService>());

        return builder.Build();
    }

    private static async Task<int> RunWorkerAsync(string? databaseConnection, params string[] arguments)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        ProcessStartInfo startInfo = CreateWorkerStartInfo(databaseConnection, arguments);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Worker did not start.");

        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> standardError = process.StandardError.ReadToEndAsync(cancellationToken);

        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));

        try
        {
            await process.WaitForExitAsync(deadline.Token);
            await Task.WhenAll(standardOutput, standardError);

            Assert.Contains("Schema migration", await standardOutput);

            return process.ExitCode;
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }

    private static ProcessStartInfo CreateWorkerStartInfo(string? databaseConnection, string[] arguments)
    {
        ProcessStartInfo startInfo = new("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = AppContext.BaseDirectory
        };

        startInfo.ArgumentList.Add(typeof(MigrationService).Assembly.Location);
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        string connectionVariable = $"ConnectionStrings__{DependencyInjection.DatabaseConnectionName}";
        startInfo.Environment.Remove(connectionVariable);

        if (databaseConnection is not null)
        {
            startInfo.Environment[connectionVariable] = databaseConnection;
        }

        return startInfo;
    }
}
