using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using Threadly.Application.Commentaries;
using Threadly.Domain.Commentaries;

namespace Threadly.Infrastructure.Persistence.Repositories;

internal sealed class CommentaryRepository(ThreadlyDbContext dbContext) : ICommentaryRepository
{
    private static readonly Expression<Func<Commentary, CommentaryDto>> Projection = commentary => new CommentaryDto(
        commentary.Id, commentary.Username, commentary.Email, commentary.Text, commentary.CreatedAtUtc);

    public async Task AddAsync(Commentary commentary, CancellationToken cancellationToken)
    {
        dbContext.Commentaries.Add(commentary);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<CommentaryDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return dbContext.Commentaries.AsNoTracking()
            .Where(commentary => commentary.Id == id)
            .Select(Projection)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<CommentaryPage> GetPageAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        int totalCount = await dbContext.Commentaries.CountAsync(cancellationToken);
        long offset = ((long)page - 1) * pageSize;

        if (offset >= totalCount)
        {
            return new CommentaryPage([], page, pageSize, totalCount);
        }

        List<CommentaryDto> items = await dbContext.Commentaries.AsNoTracking()
            .OrderByDescending(commentary => commentary.CreatedAtUtc)
            .ThenByDescending(commentary => commentary.Id)
            .Skip((int)offset)
            .Take(pageSize)
            .Select(Projection)
            .ToListAsync(cancellationToken);

        return new CommentaryPage(items, page, pageSize, totalCount);
    }
}
