namespace Threadly.Application.Commentaries;

public sealed record CommentaryPage(
    IReadOnlyList<CommentaryDto> Items, int Page, int PageSize, int TotalCount);
