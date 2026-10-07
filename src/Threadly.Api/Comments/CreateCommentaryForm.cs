using Threadly.Application.Commentaries.Attachments;
using Threadly.Application.Commentaries.CreateCommentary;

namespace Threadly.Api.Comments;

public sealed class CreateCommentaryForm : CreateCommentaryPayload
{
    public List<IFormFile> Attachments { get; init; } = [];

    public CreateCommentaryInput ToInputWithAttachments() => new(Username, Email, Text, ParentId,
        Attachments.Select(file => new AttachmentUpload(file.FileName, file.Length, file.OpenReadStream)).ToArray());
}
