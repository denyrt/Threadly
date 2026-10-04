using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Threadly.Infrastructure.Persistence;

public sealed class ThreadlyDbContextFactory : IDesignTimeDbContextFactory<ThreadlyDbContext>
{
    public ThreadlyDbContext CreateDbContext(string[] args)
    {
        string variableName = $"ConnectionStrings__{DependencyInjection.DatabaseConnectionName}";
        string? connectionString = Environment.GetEnvironmentVariable(variableName);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException($"Set {variableName} before running EF Core commands.");
        }

        DbContextOptions<ThreadlyDbContext> options = new DbContextOptionsBuilder<ThreadlyDbContext>()
            .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
            .Options;

        return new ThreadlyDbContext(options);
    }
}
