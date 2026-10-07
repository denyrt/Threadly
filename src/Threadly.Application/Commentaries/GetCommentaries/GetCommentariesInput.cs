namespace Threadly.Application.Commentaries.GetCommentaries;

public sealed record GetCommentariesInput(
    int Page = 1,
    CommentarySortBy SortBy = CommentarySortBy.Date,
    CommentarySortDirection SortDirection = CommentarySortDirection.Desc);
