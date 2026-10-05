using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using Threadly.Application.Commentaries.GetCommentaries;

namespace Threadly.Api.Comments;

public sealed class GetCommentariesPayload
{
    [FromQuery(Name = "page")]
    [Range(1, int.MaxValue, ErrorMessage = "Page must be a positive integer.")]
    public int Page { get; init; } = 1;

    public GetCommentariesInput ToInput()
    {
        return new GetCommentariesInput(Page);
    }
}
