using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Threadly.Application.Commentaries;
using Threadly.Application.Commentaries.Attachments;
using Threadly.Application.Commentaries.Content;
using Threadly.Application.Commentaries.CreateCommentary;

namespace Threadly.Api.Comments;

public sealed class CreateCommentaryForm : CommentaryIdentityPayload
{
    [Required(ErrorMessage = "Content is required.")]
    [StringLength(ContentLimits.JsonFieldLength)]
    public string Content { get; init; } = string.Empty;

    public List<IFormFile> Attachments { get; init; } = [];

    public CreateCommentaryInput ToInputWithAttachments()
    {
        IReadOnlyList<ContentBlockInput>? content;
        try
        {
            content = JsonSerializer.Deserialize<IReadOnlyList<ContentBlockInput>>(Content,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            throw new CommentaryValidationException("content", "Content must be a JSON array of text blocks with type and html fields.");
        }
        return new(Username, Email, content, ParentId,
            Attachments.Select(file => new AttachmentUpload(file.FileName, file.Length, file.OpenReadStream)).ToArray());
    }
}
