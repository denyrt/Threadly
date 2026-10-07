using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using Threadly.Application.Commentaries;
using Threadly.Application.Commentaries.GetCommentaries;

namespace Threadly.Api.Comments;

public sealed class GetCommentariesPayload
{
    [FromQuery(Name = "page")]
    [Range(1, int.MaxValue, ErrorMessage = "Page must be a positive integer.")]
    public int Page { get; init; } = 1;

    [FromQuery(Name = "sortBy")]
    [DisplayFormat(ConvertEmptyStringToNull = false)]
    [AllowedValues("date", "username", "email", ErrorMessage = "Sort by must be date, username, or email.")]
    public string SortBy { get; init; } = "date";

    [FromQuery(Name = "sortDirection")]
    [DisplayFormat(ConvertEmptyStringToNull = false)]
    [AllowedValues("asc", "desc", ErrorMessage = "Sort direction must be asc or desc.")]
    public string SortDirection { get; init; } = "desc";

    public GetCommentariesInput ToInput()
    {
        return new GetCommentariesInput(Page, SortBy switch
        {
            "date" => CommentarySortBy.Date,
            "username" => CommentarySortBy.Username,
            "email" => CommentarySortBy.Email,
            _ => throw new CommentaryValidationException("sortBy", "Sort by must be date, username, or email.")
        }, SortDirection switch
        {
            "asc" => CommentarySortDirection.Asc,
            "desc" => CommentarySortDirection.Desc,
            _ => throw new CommentaryValidationException("sortDirection", "Sort direction must be asc or desc.")
        });
    }
}
