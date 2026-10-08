using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Threadly.Api.Publication;
using Threadly.Application.Commentaries.Attachments;
using Threadly.Application.Commentaries.CreateCommentary;
using Threadly.Application.Commentaries.GetCommentaries;
using Threadly.Application.Commentaries.GetCommentaryById;
using Threadly.Application.Commentaries.GetCommentaryReplies;
using Threadly.Infrastructure;
using Threadly.Infrastructure.Captcha;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddInfrastructure();
builder.Services.AddTurnstile(builder.Configuration, builder.Environment.IsDevelopment());
builder.Services.AddPublicationProtection(builder.Configuration);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = AttachmentLimits.MaxRequestBytes);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddConcurrencyLimiter("uploads", limiter =>
    {
        limiter.PermitLimit = 2;
        limiter.QueueLimit = 0;
    });
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<CreateCommentaryUseCase>();
builder.Services.AddScoped<GetCommentaryByIdUseCase>();
builder.Services.AddScoped<GetCommentariesUseCase>();
builder.Services.AddScoped<GetCommentaryRepliesUseCase>();

builder.Services.AddControllers(options =>
    options.ModelMetadataDetailsProviders.Add(new SystemTextJsonValidationMetadataProvider()))
    .ConfigureApiBehaviorOptions(options =>
    {
        // JSON conversion errors use paths such as $.parentId; expose the same field keys as validation.
        options.InvalidModelStateResponseFactory = context =>
        {
            ValidationProblemDetails problem = new(context.ModelState);
            problem.Errors = problem.Errors
                .GroupBy(entry => System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(
                    entry.Key.StartsWith("$.", StringComparison.Ordinal) ? entry.Key[2..] : entry.Key))
                .ToDictionary(group => group.Key, group => group.SelectMany(entry => entry.Value).Distinct().ToArray());
            return new BadRequestObjectResult(problem);
        };
    });
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseForwardedHeaders();

app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = exception => exception is BadHttpRequestException requestException
        ? requestException.StatusCode : StatusCodes.Status500InternalServerError
});

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// The local Angular proxy serves HTTP and forwards its original scheme to the HTTPS API endpoint.
if (!app.Environment.IsDevelopment()) app.UseHttpsRedirection();

app.UseRouting();
app.UseMiddleware<PublicationRateLimitMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
app.MapGet("/api/captcha/config", (IOptions<TurnstileOptions> options) =>
    Results.Ok(new { siteKey = options.Value.SiteKey, action = options.Value.ExpectedAction }));

app.Run();

public partial class Program
{
}
