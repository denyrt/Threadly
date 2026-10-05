using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Threadly.Api.IntegrationTests.Fixtures;
using Threadly.Application.Commentaries;
using Threadly.Domain.Commentaries;
using Threadly.Infrastructure;
using Threadly.Infrastructure.Persistence;
using Xunit;

namespace Threadly.Api.IntegrationTests.Commentaries;

public sealed partial class CommentsApiTests(SqlServerFixture sqlServer) : IAsyncLifetime
{
    private readonly string connectionString = new SqlConnectionStringBuilder(sqlServer.ConnectionString)
    {
        InitialCatalog = $"ThreadlyHttpTests_{Guid.NewGuid():N}"
    }.ConnectionString;

    private WebApplicationFactory<Program> factory = null!;
    private HttpClient client = null!;
    private readonly SqlQueryRecorder queries = new();

    public async ValueTask InitializeAsync()
    {
        await using ThreadlyDbContext context = CreateContext();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services => services.AddDbContext<ThreadlyDbContext>(options => options.AddInterceptors(queries)));
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"ConnectionStrings:{DependencyInjection.DatabaseConnectionName}"] = connectionString
                }));
        });

        client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            client?.Dispose();
            if (factory is not null)
            {
                await factory.DisposeAsync();
            }
        }
        finally
        {
            await using ThreadlyDbContext context = CreateContext();
            await context.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task Create_ReturnsServerGeneratedValuesAndReadableLocation()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Guid suppliedId = Guid.NewGuid();
        DateTime beforeCreate = DateTime.UtcNow;

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/comments", new
        {
            username = "Denis42",
            email = "Denis@Example.com",
            text = "  Привіт, світе! 🧵\nДругий рядок.  ",
            id = suppliedId,
            createdAtUtc = "2000-01-01T00:00:00Z"
        }, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        CommentaryDto created = Assert.IsType<CommentaryDto>(
            await response.Content.ReadFromJsonAsync<CommentaryDto>(cancellationToken));
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.NotEqual(suppliedId, created.Id);
        Assert.Null(created.ParentId);
        Assert.Equal(0, created.ReplyCount);
        Assert.Equal("Denis42", created.Username);
        Assert.Equal("Denis@Example.com", created.Email);
        Assert.Equal("Привіт, світе! 🧵\nДругий рядок.", created.Text);
        Assert.Equal(DateTimeKind.Utc, created.CreatedAtUtc.Kind);
        Assert.InRange(created.CreatedAtUtc, beforeCreate, DateTime.UtcNow);

        Uri location = Assert.IsType<Uri>(response.Headers.Location);
        Assert.Equal($"/api/comments/{created.Id}", location.AbsolutePath);

        using HttpResponseMessage readResponse = await client.GetAsync(location, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);
        Assert.Equal(created, await readResponse.Content.ReadFromJsonAsync<CommentaryDto>(cancellationToken));

        CommentaryPage page = await ReadPageAsync("/api/comments");
        Assert.Equal(created, Assert.Single(page.Items));
        Assert.Equal(1, page.TotalCount);
    }

    public static TheoryData<string?, string?, string?, string, string> InvalidPayloads => new()
    {
        { null, "denis@example.com", "Text", "username", "Username is required." },
        { "   ", "denis@example.com", "Text", "username", "Username is required." },
        { "Denis_42", "denis@example.com", "Text", "username", "Username can contain only ASCII letters and digits." },
        { "Денис", "denis@example.com", "Text", "username", "Username can contain only ASCII letters and digits." },
        { new string('x', 33), "denis@example.com", "Text", "username", "Username cannot exceed 32 characters." },
        { "Denis", null, "Text", "email", "Email is required." },
        { "Denis", "   ", "Text", "email", "Email is required." },
        { "Denis", "invalid", "Text", "email", "Email must be a valid email address." },
        { "Denis", new string('x', 243) + "@example.com", "Text", "email", "Email cannot exceed 254 characters." },
        { "Denis", "denis@example.com", null, "text", "Text is required." },
        { "Denis", "denis@example.com", "   ", "text", "Text is required." },
        { "Denis", "denis@example.com", new string('x', 2001), "text", "Text cannot exceed 2000 characters." }
    };

    [Theory]
    [MemberData(nameof(InvalidPayloads))]
    public async Task Create_InvalidPayloadReturnsValidationProblemWithoutSaving(
        string? username, string? email, string? text, string field, string message)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/comments", new { username, email, text }, cancellationToken);

        ValidationProblemDetails problem = await ReadValidationProblemAsync(response);
        Assert.Contains(message, problem.Errors[field]);
        Assert.Null(response.Headers.Location);
        Assert.Empty((await ReadPageAsync("/api/comments")).Items);
    }

    [Fact]
    public async Task Create_MissingFieldsReportsAllRequiredFields()
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/comments", new { }, TestContext.Current.CancellationToken);

        ValidationProblemDetails problem = await ReadValidationProblemAsync(response);
        Assert.Equal(new[] { "email", "text", "username" }, problem.Errors.Keys.Order());
        Assert.Empty((await ReadPageAsync("/api/comments")).Items);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{invalid json}")]
    public async Task Create_InvalidBodyReturnsValidationProblem(string body)
    {
        using StringContent content = new(body, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await client.PostAsync(
            "/api/comments", content, TestContext.Current.CancellationToken);

        ValidationProblemDetails problem = await ReadValidationProblemAsync(response);
        Assert.NotEmpty(problem.Errors);
    }

    [Fact]
    public async Task Create_AcceptsMaximumFieldLengths()
    {
        string username = new('a', Commentary.MaxUsernameLength);
        string email = new string('a', 64) + "@" + new string('b', 63) + "."
            + new string('c', 63) + "." + new string('d', 57) + ".com";
        string text = new('x', Commentary.MaxTextLength);
        Assert.Equal(Commentary.MaxEmailLength, email.Length);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/comments", new { username, email, text }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task GetById_MissingCommentaryReturnsProblemDetails()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using HttpResponseMessage response = await client.GetAsync($"/api/comments/{Guid.NewGuid()}", cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        ProblemDetails problem = Assert.IsType<ProblemDetails>(
            await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken));
        Assert.Equal(404, problem.Status);
        Assert.Equal("Commentary not found.", problem.Title);
    }

    [Fact]
    public async Task GetById_InvalidIdReturnsValidationProblem()
    {
        using HttpResponseMessage response = await client.GetAsync(
            "/api/comments/invalid", TestContext.Current.CancellationToken);

        ValidationProblemDetails problem = await ReadValidationProblemAsync(response);
        Assert.Contains("id", problem.Errors.Keys);
    }

    [Theory]
    [InlineData("/api/comments", 1)]
    [InlineData("/api/comments?page=2", 2)]
    [InlineData("/api/comments?page=2147483647", int.MaxValue)]
    public async Task GetPage_EmptyDatabaseReturnsPageMetadata(string path, int expectedPage)
    {
        CommentaryPage page = await ReadPageAsync(path);

        Assert.Empty(page.Items);
        Assert.Equal(expectedPage, page.Page);
        Assert.Equal(25, page.PageSize);
        Assert.Equal(0, page.TotalCount);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("2147483648")]
    [InlineData("")]
    public async Task GetPage_InvalidPageReturnsValidationProblem(string page)
    {
        using HttpResponseMessage response = await client.GetAsync(
            $"/api/comments?page={page}", TestContext.Current.CancellationToken);

        ValidationProblemDetails problem = await ReadValidationProblemAsync(response);
        Assert.Contains("page", problem.Errors.Keys);
    }

    [Fact]
    public async Task GetPage_OrdersByUtcThenSqlIdAndReturnsBoundedPages()
    {
        DateTime timestamp = new(2026, 10, 5, 10, 0, 0, DateTimeKind.Utc);
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            ThreadlyDbContext context = scope.ServiceProvider.GetRequiredService<ThreadlyDbContext>();
            for (int index = 0; index < 56; index++)
            {
                Commentary commentary = new("Denis", "denis@example.com", $"Comment {index}",
                    index == 0 ? timestamp.AddDays(1) : timestamp);
                context.Commentaries.Add(commentary);
                context.Entry(commentary).Property(value => value.Id).CurrentValue = KnownId(index);
            }

            Commentary reply = new("Reader", "reader@example.com", "Reply excluded from root pages", timestamp, KnownId(0));
            context.Add(reply);
            context.Add(new Commentary("Reader", "reader@example.com", "Nested reply excluded too", timestamp, reply.Id));

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // The newest timestamp wins even with the lowest ID; equal timestamps use descending IDs.
        Guid[] expectedIds = new[] { KnownId(0) }
            .Concat(Enumerable.Range(1, 55).Reverse().Select(KnownId)).ToArray();

        for (int pageNumber = 1; pageNumber <= 4; pageNumber++)
        {
            CommentaryPage page = await ReadPageAsync($"/api/comments?page={pageNumber}&pageSize=100");
            Assert.Equal(pageNumber, page.Page);
            Assert.Equal(25, page.PageSize);
            Assert.Equal(56, page.TotalCount);
            Assert.Equal(expectedIds.Skip((pageNumber - 1) * 25).Take(25), page.Items.Select(value => value.Id));
            Assert.All(page.Items, value => Assert.Equal(DateTimeKind.Utc, value.CreatedAtUtc.Kind));
        }

        CommentaryPage defaultPage = await ReadPageAsync("/api/comments");
        Assert.Equal(expectedIds.Take(25), defaultPage.Items.Select(value => value.Id));
        CommentaryPage distantPage = await ReadPageAsync("/api/comments?page=2147483647");
        Assert.Empty(distantPage.Items);
        Assert.Equal(56, distantPage.TotalCount);
    }

    private async Task<CommentaryPage> ReadPageAsync(string path)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using HttpResponseMessage response = await client.GetAsync(path, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return Assert.IsType<CommentaryPage>(await response.Content.ReadFromJsonAsync<CommentaryPage>(cancellationToken));
    }

    private static async Task<ValidationProblemDetails> ReadValidationProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        ValidationProblemDetails problem = Assert.IsType<ValidationProblemDetails>(
            await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(TestContext.Current.CancellationToken));
        Assert.Equal(400, problem.Status);
        Assert.Equal("One or more validation errors occurred.", problem.Title);

        return problem;
    }

    private ThreadlyDbContext CreateContext()
    {
        DbContextOptions<ThreadlyDbContext> options = new DbContextOptionsBuilder<ThreadlyDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new ThreadlyDbContext(options);
    }

    private static Guid KnownId(int index)
    {
        return Guid.Parse($"00000000-0000-7000-8000-{index:x12}");
    }
}
