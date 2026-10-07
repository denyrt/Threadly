using Threadly.Application.Commentaries.Attachments;
using Threadly.Application.Commentaries.Content;
using Threadly.Domain.Commentaries;

namespace Threadly.Application.Commentaries.CreateCommentary;

public sealed class CreateCommentaryUseCase(
    ICommentaryRepository repository, TimeProvider timeProvider, IAttachmentProcessor attachmentProcessor, IContentProcessor contentProcessor)
{
    public async Task<CommentaryDto> ExecuteAsync(CreateCommentaryInput input, CancellationToken cancellationToken)
    {
        ProcessedContent processed = contentProcessor.Process(input.Content);
        Commentary commentary = new(
            input.Username, input.Email, processed.Content, timeProvider.GetUtcNow().UtcDateTime, input.ParentId);

        if (input.ParentId is Guid parentId && !await repository.ExistsAsync(parentId, cancellationToken))
        {
            throw new CommentaryValidationException("parentId", "The parent comment does not exist.");
        }

        IReadOnlyList<ProcessedAttachment> attachments = await attachmentProcessor.ProcessAsync(input.Attachments ?? [], cancellationToken);
        foreach (ProcessedAttachment attachment in attachments)
        {
            commentary.AddAttachment(attachment.FileName, attachment.ContentType, attachment.Content, attachment.Width, attachment.Height);
        }

        await repository.AddAsync(commentary, cancellationToken);

        return CommentaryDto.From(commentary);
    }
}
