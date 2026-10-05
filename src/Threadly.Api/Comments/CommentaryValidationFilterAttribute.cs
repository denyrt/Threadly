using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Threadly.Application.Commentaries;

namespace Threadly.Api.Comments;

public sealed class CommentaryValidationFilterAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is CommentaryValidationException exception)
        {
            ValidationProblemDetails problem = new(new Dictionary<string, string[]>
            {
                [exception.Field] = [exception.Message]
            })
            {
                Status = StatusCodes.Status400BadRequest
            };
            context.Result = new BadRequestObjectResult(problem);
            context.ExceptionHandled = true;
        }
    }
}
