using Threadly.Domain.Commentaries;

namespace Threadly.Application.Commentaries.Attachments;

public sealed record AttachmentDto(Guid Id, string FileName, string ContentType, int Size, int? Width, int? Height)
{
    public static AttachmentDto From(CommentaryAttachment attachment) => new(
        attachment.Id, attachment.FileName, attachment.ContentType, attachment.Size, attachment.Width, attachment.Height);
}

public sealed record AttachmentContent(string FileName, string ContentType, byte[] Content);

public interface IAttachmentRepository
{
    Task<AttachmentContent?> GetAsync(Guid commentaryId, Guid attachmentId, CancellationToken cancellationToken);
}
