using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Threadly.Application.Commentaries.Content;

namespace Threadly.Api.Comments;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PreviewCommentaryPayload
{
    [Required(ErrorMessage = "Content is required.")]
    public IReadOnlyList<ContentBlockInput>? Content { get; init; }
}
