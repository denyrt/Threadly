using Threadly.Application.Commentaries.Attachments;
using Threadly.Application.Commentaries.Content;

namespace Threadly.Application.Commentaries.CreateCommentary;

public sealed record CreateCommentaryInput(string Username, string Email, IReadOnlyList<ContentBlockInput>? Content, Guid? ParentId = null,
    IReadOnlyList<AttachmentUpload>? Attachments = null);
