using Threadly.Application.Commentaries.Attachments;

namespace Threadly.Application.Commentaries.CreateCommentary;

public sealed record CreateCommentaryInput(string Username, string Email, string Text, Guid? ParentId = null,
    IReadOnlyList<AttachmentUpload>? Attachments = null);
