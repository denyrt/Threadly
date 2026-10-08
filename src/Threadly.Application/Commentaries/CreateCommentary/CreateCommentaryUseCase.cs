using Threadly.Application.Commentaries.Attachments;
using Threadly.Application.Commentaries.Captcha;
using Threadly.Application.Commentaries.Content;
using Threadly.Domain.Commentaries;

namespace Threadly.Application.Commentaries.CreateCommentary;

public sealed class CreateCommentaryUseCase(
    ICommentaryRepository repository, TimeProvider timeProvider, IAttachmentProcessor attachmentProcessor,
    IContentProcessor contentProcessor, ICaptchaVerifier captchaVerifier)
{
    public async Task<CommentaryDto> ExecuteAsync(CreateCommentaryInput input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.CaptchaToken) || input.CaptchaToken.Length > 2048)
        {
            throw new CommentaryValidationException("captchaToken", "Complete verification before posting.");
        }
        ProcessedContent processed = contentProcessor.Process(input.Content);
        Commentary commentary = new(
            input.Username, input.Email, processed.Content, timeProvider.GetUtcNow().UtcDateTime, input.ParentId);

        if (input.ParentId is Guid parentId && !await repository.ExistsAsync(parentId, cancellationToken))
        {
            throw new CommentaryValidationException("parentId", "The parent comment does not exist.");
        }

        CaptchaVerification verification = await captchaVerifier.VerifyAsync(input.CaptchaToken, cancellationToken);
        if (verification == CaptchaVerification.Unavailable) throw new CaptchaUnavailableException();
        if (verification != CaptchaVerification.Valid)
        {
            throw new CommentaryValidationException("captchaToken", "Verification expired or failed. Please verify again.");
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
