using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Threadly.Infrastructure.Persistence;

namespace Threadly.Infrastructure;

public static class DependencyInjection
{
    public const string DatabaseConnectionName = "threadlydb";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddDbContext<ThreadlyDbContext>((provider, options) =>
        {
            IConfiguration configuration = provider.GetRequiredService<IConfiguration>();
            string? connectionString = configuration.GetConnectionString(DatabaseConnectionName);

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException($"Connection string '{DatabaseConnectionName}' is required.");
            }

            options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure());
        });

        return services;
    }
}
