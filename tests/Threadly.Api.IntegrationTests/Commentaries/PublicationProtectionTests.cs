using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Threadly.Api.Publication;
using Threadly.Infrastructure.Captcha;
using Xunit;

namespace Threadly.Api.IntegrationTests.Commentaries;

public sealed partial class CommentsApiTests
{
    [Fact]
    public async Task RootAndReplyShareTheSamePublicationBudget()
    {
        await using WebApplicationFactory<Program> limited = LimitFactory(2);
        using HttpClient limitedClient = limited.CreateClient();
        using HttpContent rootBody = PublicationBody(false, "test-token");
        using HttpResponseMessage root = await limitedClient.PostAsync("/api/comments", rootBody, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, root.StatusCode);
        JsonElement created = await root.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        using MultipartFormDataContent replyBody = CommentForm(created.GetProperty("id").GetGuid());
        using HttpResponseMessage reply = await limitedClient.PostAsync("/api/comments", replyBody, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, reply.StatusCode);
        using HttpResponseMessage blocked = await limitedClient.PostAsync("/api/comments", rootBody, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
    }

    [Fact]
    public async Task UnsupportedContentTypeAlsoSpendsPublicationBudget()
    {
        await using WebApplicationFactory<Program> limited = LimitFactory(1);
        using HttpClient limitedClient = limited.CreateClient();
        using StringContent body = new("invalid content type");
        using HttpResponseMessage first = await limitedClient.PostAsync("/api/comments", body, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, first.StatusCode);
        using HttpContent json = PublicationBody(false, "test-token");
        using HttpResponseMessage blocked = await limitedClient.PostAsync("/api/comments", json, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(false, "")]
    [InlineData(true, " ")]
    [InlineData(false, "invalid")]
    [InlineData(true, "expired")]
    [InlineData(false, "long")]
    [InlineData(true, "long")]
    public async Task CaptchaFailureReturnsFieldErrorWithoutWriting(bool multipart, string? token)
    {
        if (token == "long") token = new string('x', 2049);
        using HttpContent body = PublicationBody(multipart, token);
        using HttpResponseMessage response = await client.PostAsync("/api/comments", body, TestContext.Current.CancellationToken);
        Assert.Contains("captchaToken", (await ReadValidationProblemAsync(response)).Errors.Keys);
        await AssertNoPublication();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnavailableCaptchaReturns503AndDoesNotWrite(bool multipart)
    {
        using HttpContent body = PublicationBody(multipart, "unavailable");
        using HttpResponseMessage response = await client.PostAsync("/api/comments", body, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, captcha.Calls);
        await AssertNoPublication();
    }

    [Fact]
    public async Task PreviewAndPublicConfigurationDoNotInvokeCaptchaOrExposeSecret()
    {
        using HttpResponseMessage preview = await client.PostAsJsonAsync("/api/comments/preview",
            new { content = new[] { new { type = "text", html = "preview" } } }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        using HttpResponseMessage config = await client.GetAsync("/api/captcha/config", TestContext.Current.CancellationToken);
        JsonElement json = await config.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(["action", "siteKey"], json.EnumerateObject().Select(property => property.Name).Order());
        Assert.Equal(0, captcha.Calls);
    }

    [Fact]
    public async Task JsonAndMultipartFailuresShareBudgetButReadAndPreviewDoNot()
    {
        await using WebApplicationFactory<Program> limited = LimitFactory(2);
        using HttpClient limitedClient = limited.CreateClient();
        using HttpContent json = PublicationBody(false, "invalid");
        using HttpContent multipart = PublicationBody(true, "invalid");
        using HttpResponseMessage first = await limitedClient.PostAsync("/api/comments", json, TestContext.Current.CancellationToken);
        using HttpResponseMessage second = await limitedClient.PostAsync("/api/comments", multipart, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        using HttpResponseMessage blocked = await limitedClient.PostAsync("/api/comments", json, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.True(blocked.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Contains("publication_rate_limit", await blocked.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        using HttpResponseMessage read = await limitedClient.GetAsync("/api/comments", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using HttpResponseMessage preview = await limitedClient.PostAsJsonAsync("/api/comments/preview",
            new { content = new[] { new { type = "text", html = "preview" } } }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal(2, captcha.Calls);
    }

    [Fact]
    public async Task TrustedProxyUsesClientIpUntrustedHeadersCannotChangeBudgetAndMappedAddressesNormalize()
    {
        await using WebApplicationFactory<Program> limited = LimitFactory(1).WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.PostConfigure<TrustedProxyOptions>(options => options.KnownProxies = ["10.0.0.2"])));
        _ = limited.CreateClient();
        Assert.Equal(400, await SendFrom(limited, "10.0.0.2", "198.51.100.1"));
        Assert.Equal(400, await SendFrom(limited, "10.0.0.2", "198.51.100.2"));
        Assert.Equal(429, await SendFrom(limited, "10.0.0.2", "198.51.100.1"));
        Assert.Equal(400, await SendFrom(limited, "203.0.113.5", "198.51.100.3"));
        Assert.Equal(429, await SendFrom(limited, "203.0.113.5", "198.51.100.4"));
        Assert.Equal(429, await SendFrom(limited, "::ffff:203.0.113.5", "198.51.100.5"));
        // Only the trusted nearest hop is consumed; a forged earlier entry never becomes the key.
        Assert.Equal(429, await SendFrom(limited, "10.0.0.2", "192.0.2.200, 198.51.100.1", "http, https"));
    }

    [Fact]
    public async Task UploadConcurrencyAndPublicationBudgetApplyTogether()
    {
        await using WebApplicationFactory<Program> limited = LimitFactory(4);
        using HttpClient limitedClient = limited.CreateClient();
        using HttpContent firstBody = PublicationBody(true, "block");
        using HttpContent secondBody = PublicationBody(true, "block");
        Task<HttpResponseMessage> first = limitedClient.PostAsync("/api/comments", firstBody, TestContext.Current.CancellationToken);
        Task<HttpResponseMessage> second = limitedClient.PostAsync("/api/comments", secondBody, TestContext.Current.CancellationToken);
        try
        {
            await captcha.TwoRequestsEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            using HttpContent body = PublicationBody(true, "invalid");
            using HttpResponseMessage busy = await limitedClient.PostAsync("/api/comments", body, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.TooManyRequests, busy.StatusCode);
            Assert.Null(busy.Headers.RetryAfter);
            Assert.Contains("upload_concurrency", await busy.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            using HttpContent json = PublicationBody(false, "invalid");
            using HttpResponseMessage fourth = await limitedClient.PostAsync("/api/comments", json, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, fourth.StatusCode);
            using HttpResponseMessage fifth = await limitedClient.PostAsync("/api/comments", json, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.TooManyRequests, fifth.StatusCode);
            Assert.NotNull(fifth.Headers.RetryAfter);
        }
        finally
        {
            captcha.Release.TrySetResult();
            (await first).Dispose();
            (await second).Dispose();
        }
    }

    [Fact]
    public void InvalidProductionConfigurationFailsAtStartup()
    {
        using WebApplicationFactory<Program> invalid = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureServices(services => services.PostConfigure<TurnstileOptions>(options => options.SecretKey = ""));
        });
        Assert.Throws<OptionsValidationException>(() => invalid.CreateClient());
    }

    private WebApplicationFactory<Program> LimitFactory(int permits) => factory.WithWebHostBuilder(builder =>
        builder.ConfigureServices(services => services.PostConfigure<PublicationOptions>(options => options.PermitLimit = permits)));

    private static async Task<int> SendFrom(WebApplicationFactory<Program> host, string peer, string forwarded, string scheme = "https")
    {
        using HttpContent body = PublicationBody(false, "invalid");
        byte[] bytes = await body.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        HttpContext response = await host.Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
            context.Request.Scheme = "https";
            context.Request.Method = "POST";
            context.Request.Path = "/api/comments";
            context.Request.ContentType = "application/json";
            context.Request.ContentLength = bytes.Length;
            context.Request.Body = new MemoryStream(bytes);
            context.Request.Headers["X-Forwarded-For"] = forwarded;
            context.Request.Headers["X-Forwarded-Proto"] = scheme;
        }, TestContext.Current.CancellationToken);
        return response.Response.StatusCode;
    }

    private static HttpContent PublicationBody(bool multipart, string? token)
    {
        if (!multipart) return JsonContent.Create(new
        {
            username = "Reader",
            email = "reader@example.com",
            captchaToken = token,
            content = new[] { new { type = "text", html = "hello" } }
        });
        MultipartFormDataContent form = new();
        form.Add(new StringContent("Reader"), "username");
        form.Add(new StringContent("reader@example.com"), "email");
        form.Add(new StringContent("[{\"type\":\"text\",\"html\":\"hello\"}]"), "content");
        if (token is not null) form.Add(new StringContent(token), "captchaToken");
        AddFile(form, "notes.txt", Encoding.UTF8.GetBytes("hello"));
        return form;
    }
}
