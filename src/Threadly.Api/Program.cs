using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Threadly.Application.Commentaries.CreateCommentary;
using Threadly.Application.Commentaries.GetCommentaries;
using Threadly.Application.Commentaries.GetCommentaryById;
using Threadly.Application.Commentaries.GetCommentaryReplies;
using Threadly.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddInfrastructure();

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
            problem.Errors = problem.Errors.ToDictionary(
                entry => entry.Key.StartsWith("$.", StringComparison.Ordinal) ? entry.Key[2..] : entry.Key,
                entry => entry.Value);
            return new BadRequestObjectResult(problem);
        };
    });
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program
{
}
