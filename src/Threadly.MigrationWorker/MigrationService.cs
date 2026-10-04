using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Threadly.Infrastructure.Persistence;

namespace Threadly.MigrationWorker;

public sealed class MigrationService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    IHostApplicationLifetime lifetime,
    ILogger<MigrationService> logger) : BackgroundService
{
    // Shutdown before completion must remain a failed run.
    public int ExitCode { get; private set; } = 1;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

        try
        {
            TimeSpan timeout = configuration.GetValue("Migration:Timeout", TimeSpan.FromMinutes(2));

            if (timeout <= TimeSpan.Zero)
            {
                throw new InvalidOperationException("Migration timeout must be positive.");
            }

            deadline.CancelAfter(timeout);
            deadline.Token.ThrowIfCancellationRequested();

            logger.LogInformation("Applying SQL Server schema migrations.");

            await using (AsyncServiceScope scope = scopeFactory.CreateAsyncScope())
            {
                ThreadlyDbContext dbContext = scope.ServiceProvider.GetRequiredService<ThreadlyDbContext>();
                await dbContext.Database.MigrateAsync(deadline.Token);
            }

            deadline.Token.ThrowIfCancellationRequested();
            logger.LogInformation("Schema migrations completed successfully.");
            ExitCode = 0;
        }
        catch (Exception error)
        {
            // Provider messages can contain data; record only the type and SQL error number.
            logger.LogError(
                "Schema migration failed ({ErrorType}, SQL error {SqlNumber}).",
                error.GetType().Name,
                FindSqlErrorNumber(error));
        }
        finally
        {
            lifetime.StopApplication();
        }
    }

    private static int? FindSqlErrorNumber(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            if (current is SqlException sqlException)
            {
                return sqlException.Number;
            }
        }

        return null;
    }
}
