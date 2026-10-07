using Microsoft.EntityFrameworkCore;
using Threadly.Domain.Commentaries;

namespace Threadly.Infrastructure.Persistence;

public sealed class ThreadlyDbContext(DbContextOptions<ThreadlyDbContext> options) : DbContext(options)
{
    public DbSet<Commentary> Commentaries => Set<Commentary>();
    public DbSet<CommentaryAttachment> CommentaryAttachments => Set<CommentaryAttachment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ThreadlyDbContext).Assembly);
    }
}
