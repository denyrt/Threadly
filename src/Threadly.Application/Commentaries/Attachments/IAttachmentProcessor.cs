namespace Threadly.Application.Commentaries.Attachments;

public sealed record AttachmentUpload(string FileName, long Length, Func<Stream> OpenReadStream);

public sealed record ProcessedAttachment(string FileName, string ContentType, byte[] Content, int? Width, int? Height);

public interface IAttachmentProcessor
{
    Task<IReadOnlyList<ProcessedAttachment>> ProcessAsync(
        IReadOnlyList<AttachmentUpload> uploads, CancellationToken cancellationToken);
}
