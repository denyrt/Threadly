using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using Threadly.Application.Commentaries;
using Threadly.Application.Commentaries.Attachments;
using Threadly.Domain.Commentaries;

namespace Threadly.Infrastructure.Persistence.Repositories;

internal sealed class CommentaryRepository(ThreadlyDbContext dbContext) : ICommentaryRepository
{
    private Expression<Func<Commentary, CommentaryDto>> Projection => commentary => new CommentaryDto(
        commentary.Id, commentary.Username, commentary.Email, commentary.Content, commentary.CreatedAtUtc,
        commentary.ParentId, dbContext.Commentaries.Count(reply => reply.ParentId == commentary.Id),
        commentary.Attachments.OrderBy(attachment => attachment.Position).Select(attachment => new AttachmentDto(
            attachment.Id, attachment.FileName, attachment.ContentType, attachment.Size, attachment.Width, attachment.Height)).ToList());

    public async Task AddAsync(Commentary commentary, CancellationToken cancellationToken)
    {
        dbContext.Commentaries.Add(commentary);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 547 } sqlException
            && sqlException.Message.Contains("FK_Commentaries_Commentaries_ParentId", StringComparison.Ordinal))
        {
            // The parent can disappear between the existence check and the insert.
            foreach (CommentaryAttachment attachment in commentary.Attachments)
            {
                dbContext.Entry(attachment).State = EntityState.Detached;
            }
            dbContext.Entry(commentary).State = EntityState.Detached;
            throw new CommentaryValidationException("parentId", "The parent comment does not exist.");
        }
    }

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken)
    {
        return dbContext.Commentaries.AnyAsync(commentary => commentary.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<ReplyCountDto>> GetReplyCountsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        return await dbContext.Commentaries.AsNoTracking().Where(comment => ids.Contains(comment.Id))
            .Select(comment => new ReplyCountDto(comment.Id, dbContext.Commentaries.Count(reply => reply.ParentId == comment.Id)))
            .ToListAsync(cancellationToken);
    }

    public Task<CommentaryDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return dbContext.Commentaries.AsNoTracking()
            .Where(commentary => commentary.Id == id)
            .Select(Projection)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<CommentaryPage> GetPageAsync(int page, int pageSize, CommentarySortBy sortBy,
        CommentarySortDirection sortDirection, CancellationToken cancellationToken)
    {
        IQueryable<Commentary> roots = dbContext.Commentaries.AsNoTracking().Where(commentary => commentary.ParentId == null);
        int totalCount = await roots.CountAsync(cancellationToken);
        long offset = ((long)page - 1) * pageSize;

        if (offset >= totalCount)
        {
            return new CommentaryPage([], page, pageSize, totalCount);
        }

        IOrderedQueryable<Commentary> ordered = (sortBy, sortDirection) switch
        {
            (CommentarySortBy.Date, CommentarySortDirection.Asc) => roots.OrderBy(value => value.CreatedAtUtc).ThenBy(value => value.Id),
            (CommentarySortBy.Date, CommentarySortDirection.Desc) => roots.OrderByDescending(value => value.CreatedAtUtc).ThenByDescending(value => value.Id),
            (CommentarySortBy.Username, CommentarySortDirection.Asc) => roots.OrderBy(value => value.Username).ThenBy(value => value.Id),
            (CommentarySortBy.Username, CommentarySortDirection.Desc) => roots.OrderByDescending(value => value.Username).ThenByDescending(value => value.Id),
            (CommentarySortBy.Email, CommentarySortDirection.Asc) => roots.OrderBy(value => value.Email).ThenBy(value => value.Id),
            (CommentarySortBy.Email, CommentarySortDirection.Desc) => roots.OrderByDescending(value => value.Email).ThenByDescending(value => value.Id),
            _ => throw new ArgumentOutOfRangeException(nameof(sortBy), "Unknown comment ordering.")
        };

        List<CommentaryDto> items = await ordered
            .Skip((int)offset)
            .Take(pageSize)
            .Select(Projection)
            .ToListAsync(cancellationToken);

        return new CommentaryPage(items, page, pageSize, totalCount);
    }

    public async Task<CommentaryReplies> GetRepliesAsync(
        Guid parentId, ReplyCursor? cursor, int pageSize, CancellationToken cancellationToken)
    {
        IQueryable<Commentary> replies = dbContext.Commentaries.AsNoTracking().Where(commentary => commentary.ParentId == parentId);
        if (cursor is not null)
        {
            if (!await replies.AnyAsync(reply => reply.Id == cursor.Id && reply.CreatedAtUtc == cursor.CreatedAtUtc, cancellationToken))
            {
                throw new CommentaryValidationException("cursor", "The cursor no longer identifies a reply to this comment.");
            }

            replies = replies.Where(reply => reply.CreatedAtUtc > cursor.CreatedAtUtc
                || (reply.CreatedAtUtc == cursor.CreatedAtUtc && reply.Id.CompareTo(cursor.Id) > 0));
        }

        List<CommentaryDto> items = await replies
            .OrderBy(reply => reply.CreatedAtUtc).ThenBy(reply => reply.Id)
            .Take(pageSize + 1).Select(Projection).ToListAsync(cancellationToken);

        string? nextCursor = null;
        if (items.Count > pageSize)
        {
            items.RemoveAt(pageSize);
            CommentaryDto last = items[^1];
            nextCursor = new ReplyCursor(parentId, last.CreatedAtUtc, last.Id).Encode();
        }

        return new CommentaryReplies(items, nextCursor);
    }
}
