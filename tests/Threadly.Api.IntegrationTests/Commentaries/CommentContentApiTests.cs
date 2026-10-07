using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Threadly.Application.Commentaries;
using Threadly.Application.Commentaries.Content;
using Threadly.Infrastructure.Persistence;
using Xunit;

namespace Threadly.Api.IntegrationTests.Commentaries;

public sealed partial class CommentsApiTests
{
    [Fact]
    public async Task PreviewMatchesCreateWithoutWritesAndCreateRevalidates()
    {
        var content = new[] { new { type = "text", html = "<i>Привіт</i> <a href='https://example.com/?a=1&amp;b=2'>link</a>\n<code>&lt;x&gt;&amp;</code>" } };
        queries.Commands.Clear();
        using HttpResponseMessage previewResponse = await client.PostAsJsonAsync("/api/comments/preview", new { content }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        Assert.Empty(queries.Commands);
        ProcessedContent preview = (await previewResponse.Content.ReadFromJsonAsync<ProcessedContent>(TestContext.Current.CancellationToken))!;
        Assert.Equal(content[0].html.Length, preview.BudgetUsed);
        Assert.Equal(2000, preview.BudgetLimit);
        await AssertNoPublication();
        using HttpResponseMessage createResponse = await client.PostAsJsonAsync("/api/comments", new { username = "Reader", email = "reader@example.com", content }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        CommentaryDto created = (await createResponse.Content.ReadFromJsonAsync<CommentaryDto>(TestContext.Current.CancellationToken))!;
        Assert.Equivalent(preview.Content, created.Content);

        using HttpResponseMessage badCreate = await client.PostAsJsonAsync("/api/comments", new { username = "Reader", email = "reader@example.com", content = new[] { new { type = "text", html = "<i>changed" } } }, TestContext.Current.CancellationToken);
        Assert.Contains("content", (await ReadValidationProblemAsync(badCreate)).Errors.Keys);
        Assert.Equal(1, (await ReadPageAsync("/api/comments")).TotalCount);
    }

    [Theory]
    [InlineData("<i>unclosed")]
    [InlineData("<i><strong>x</i></strong>")]
    [InlineData("<script>x</script>")]
    [InlineData("<a href='jav&#x61;script:alert(1)'>x</a>")]
    [InlineData("<i>&nbsp;</i>")]
    public async Task InvalidContentHasSamePolicyEverywhereAndLeavesNoAttachments(string html)
    {
        var content = new[] { new { type = "text", html } };
        using HttpResponseMessage preview = await client.PostAsJsonAsync("/api/comments/preview", new { content }, TestContext.Current.CancellationToken);
        string[] expected = (await ReadValidationProblemAsync(preview)).Errors["content"];
        using HttpResponseMessage create = await client.PostAsJsonAsync("/api/comments", new { username = "Reader", email = "reader@example.com", content }, TestContext.Current.CancellationToken);
        Assert.Equal(expected, (await ReadValidationProblemAsync(create)).Errors["content"]);
        using MultipartFormDataContent form = ContentForm(JsonSerializer.Serialize(content));
        // Invalid image bytes must never reach the attachment processor before HTML validation.
        AddFile(form, "bad.png", "not an image"u8.ToArray());
        using HttpResponseMessage upload = await client.PostAsync("/api/comments", form, TestContext.Current.CancellationToken);
        Assert.Equal(expected, (await ReadValidationProblemAsync(upload)).Errors["content"]);
        await AssertNoPublication();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("[null]")]
    [InlineData("[{\"type\":\"quote\",\"html\":\"x\"}]")]
    [InlineData("[{\"type\":\"unknown\",\"html\":\"x\"}]")]
    [InlineData("[{\"type\":\"text\",\"html\":12}]")]
    [InlineData("[{\"type\":\"text\",\"html\":\"x\",\"extra\":1}]")]
    [InlineData("{\"type\":\"text\",\"html\":\"x\"}")]
    public async Task JsonAndMultipartRejectInvalidStructure(string json)
    {
        using StringContent body = new("{\"username\":\"Reader\",\"email\":\"reader@example.com\",\"content\":" + json + "}", System.Text.Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await client.PostAsync("/api/comments", body, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using MultipartFormDataContent form = ContentForm(json);
        using HttpResponseMessage multipart = await client.PostAsync("/api/comments", form, TestContext.Current.CancellationToken);
        Assert.Contains("content", (await ReadValidationProblemAsync(multipart)).Errors.Keys);
        await AssertNoPublication();
    }

    [Fact]
    public async Task MultipartAcceptsEscapedBudgetBoundaryForRootAndReplyWithFiles()
    {
        string html = new('я', 2000);
        string json = JsonSerializer.Serialize(new[] { new { type = "text", html } });
        Assert.True(json.Length > 8192);
        Guid? parentId = null;
        for (int index = 0; index < 2; index++)
        {
            using MultipartFormDataContent form = ContentForm(json, parentId);
            AddFile(form, "notes.txt", "notes"u8.ToArray());
            CommentaryDto result = await PostForm(form);
            Assert.Equal(parentId, result.ParentId);
            Assert.Equal(html, result.Content[0].Html);
            Assert.Single(result.Attachments);
            parentId = result.Id;
        }
        using MultipartFormDataContent tooLong = ContentForm(JsonSerializer.Serialize(new[] { new { type = "text", html = html + "x" } }));
        using HttpResponseMessage rejected = await client.PostAsync("/api/comments", tooLong, TestContext.Current.CancellationToken);
        Assert.Contains("content", (await ReadValidationProblemAsync(rejected)).Errors.Keys);
    }

    [Fact]
    public async Task PreviewRejectsAttachmentsAndMultipart()
    {
        using HttpResponseMessage json = await client.PostAsJsonAsync("/api/comments/preview", new { content = new[] { new { type = "text", html = "text" } }, attachments = new[] { "bytes" } }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, json.StatusCode);
        using MultipartFormDataContent form = CommentForm();
        AddFile(form, "notes.txt", "notes"u8.ToArray());
        using HttpResponseMessage multipart = await client.PostAsync("/api/comments/preview", form, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, multipart.StatusCode);
        await AssertNoPublication();
    }

    [Fact]
    public async Task ContentRoundTripsInOrderAndAllowsExpandedHtml()
    {
        var content = new[] { new { type = "text", html = new string('&', 1990) }, new { type = "text", html = "\nsecond" } };
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/comments", new { username = "Reader", email = "reader@example.com", content }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        CommentaryDto created = (await response.Content.ReadFromJsonAsync<CommentaryDto>(TestContext.Current.CancellationToken))!;
        Assert.True(created.Content[0].Html.Length > 2000);
        Assert.Equal("\nsecond", created.Content[1].Html);
        await using ThreadlyDbContext db = CreateContext();
        Assert.Equivalent(created.Content, (await db.Commentaries.SingleAsync(TestContext.Current.CancellationToken)).Content);
        Assert.Equivalent(created.Content, Assert.Single((await ReadPageAsync("/api/comments")).Items).Content);
    }

    private static MultipartFormDataContent ContentForm(string json, Guid? parentId = null)
    {
        MultipartFormDataContent form = new();
        form.Add(new StringContent("Reader"), "username");
        form.Add(new StringContent("reader@example.com"), "email");
        form.Add(new StringContent(json), "content");
        if (parentId.HasValue) form.Add(new StringContent(parentId.Value.ToString()), "parentId");
        return form;
    }
}
