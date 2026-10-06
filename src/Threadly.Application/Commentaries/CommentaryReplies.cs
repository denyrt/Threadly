namespace Threadly.Application.Commentaries;

public sealed record CommentaryReplies(IReadOnlyList<CommentaryDto> Items, string? NextCursor);
