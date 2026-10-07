using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Threadly.Api.Comments;

public sealed class ReadUploadFormAttribute : Attribute, IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        try
        {
            // Read after the request/form limit filters, but before MVC turns IO errors into
            // string-only model errors. Preserve 413 for both Content-Length and chunked uploads.
            await context.HttpContext.Request.ReadFormAsync(context.HttpContext.RequestAborted);
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status413PayloadTooLarge,
                Title = "The request exceeds the 21 MiB upload limit."
            })
            { StatusCode = StatusCodes.Status413PayloadTooLarge };
            return;
        }
        catch (InvalidDataException)
        {
            context.Result = new BadRequestObjectResult(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["attachments"] = ["The upload form is malformed or exceeds its limits."]
            })
            { Status = StatusCodes.Status400BadRequest });
            return;
        }
        await next();
    }
}
