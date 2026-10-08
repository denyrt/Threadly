using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Threadly.Application.Commentaries;
using Threadly.Application.Commentaries.Captcha;

namespace Threadly.Api.Comments;

public sealed class CommentaryValidationFilterAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is CaptchaUnavailableException unavailable)
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = unavailable.Message
            })
            { StatusCode = StatusCodes.Status503ServiceUnavailable };
            context.ExceptionHandled = true;
            return;
        }
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
