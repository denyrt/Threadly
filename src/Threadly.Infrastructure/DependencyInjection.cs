using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Threadly.Application.Commentaries;
using Threadly.Application.Commentaries.Attachments;
using Threadly.Application.Commentaries.Content;
using Threadly.Infrastructure.Attachments;
using Threadly.Infrastructure.Content;
using Threadly.Infrastructure.Persistence;
using Threadly.Infrastructure.Persistence.Repositories;

namespace Threadly.Infrastructure;

public static class DependencyInjection
{
    public const string DatabaseConnectionName = "threadlydb";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<ICommentaryRepository, CommentaryRepository>();
        services.AddScoped<IAttachmentRepository, AttachmentRepository>();
        services.AddSingleton<IAttachmentProcessor, AttachmentProcessor>();
        services.AddSingleton<IContentProcessor, ContentProcessor>();

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
