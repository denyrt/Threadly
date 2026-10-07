using Microsoft.EntityFrameworkCore;
using Threadly.Application.Commentaries.Attachments;

namespace Threadly.Infrastructure.Persistence.Repositories;

internal sealed class AttachmentRepository(ThreadlyDbContext dbContext) : IAttachmentRepository
{
    public Task<AttachmentContent?> GetAsync(Guid commentaryId, Guid attachmentId, CancellationToken cancellationToken) =>
        dbContext.CommentaryAttachments.AsNoTracking()
            .Where(value => value.Id == attachmentId && value.CommentaryId == commentaryId)
            .Select(value => new AttachmentContent(value.FileName, value.ContentType, value.Content))
            .SingleOrDefaultAsync(cancellationToken);
}
