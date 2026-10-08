using ImageMagick;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using Threadly.Application.Commentaries;
using Threadly.Application.Commentaries.Attachments;
using Threadly.Domain.Commentaries;
using Threadly.Infrastructure.Persistence;
using Xunit;

namespace Threadly.Api.IntegrationTests.Commentaries;

public sealed partial class CommentsApiTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task UploadRequestLimit_IsEnforcedByKestrel(bool multipart, bool chunked)
    {
        await using WebApplicationFactory<Program> kestrel = factory.WithWebHostBuilder(builder => builder.UseUrls("http://127.0.0.1:0"));
        kestrel.UseKestrel(0);
        using HttpClient networkClient = kestrel.CreateClient();
        using HttpContent body = multipart ? CommentForm()
            : new ByteArrayContent(Enumerable.Repeat((byte)' ', AttachmentLimits.MaxRequestBytes + 1).ToArray());
        if (body is MultipartFormDataContent form)
        {
            AddFile(form, "large.txt", new byte[AttachmentLimits.MaxRequestBytes + 1]);
        }
        else
        {
            body.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }
        using HttpRequestMessage request = new(HttpMethod.Post, "/api/comments") { Content = body };
        request.Headers.TransferEncodingChunked = chunked;
        request.Headers.ExpectContinue = true;
        using HttpResponseMessage response = await networkClient.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.RequestEntityTooLarge,
            $"{response.StatusCode}: {await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)}");
        await AssertNoPublication();
    }

    [Fact]
    public async Task Attachments_RootAndReplyCanBeCreatedReadAndDownloaded()
    {
        using MagickImage image = new(MagickColors.Orange, 800, 400);
        byte[] text = Encoding.UTF8.GetBytes("Привіт 🧵\nThis stays plain text: <script>alert(1)</script>");
        using MultipartFormDataContent rootForm = CommentForm();
        // The response type is derived from decoded bytes, never this untrusted MIME header.
        AddFile(rootForm, "photo.png", image.ToByteArray(MagickFormat.Png), "application/octet-stream");
        AddFile(rootForm, "notes.txt", text, "text/html");
        CommentaryDto root = await PostForm(rootForm);
        Assert.Equal(2, root.Attachments.Count);
        AttachmentDto picture = root.Attachments[0];
        Assert.Equal("image/png", picture.ContentType);
        Assert.Equal(320, picture.Width);
        Assert.Equal(160, picture.Height);
        Assert.Equal("notes.txt", root.Attachments[1].FileName);

        using MultipartFormDataContent replyForm = CommentForm(root.Id);
        AddFile(replyForm, "reply.txt", text);
        CommentaryDto reply = await PostForm(replyForm);
        Assert.Equal(root.Id, reply.ParentId);
        Assert.Equivalent(reply, Assert.Single((await ReadRepliesAsync(root.Id)).Items));

        queries.Commands.Clear();
        CommentaryDto readRoot = (await client.GetFromJsonAsync<CommentaryDto>($"/api/comments/{root.Id}", TestContext.Current.CancellationToken))!;
        Assert.Equivalent(root.Attachments, readRoot.Attachments);
        Assert.Equivalent(root.Attachments, Assert.Single((await ReadPageAsync("/api/comments")).Items).Attachments);
        // Content now also names comment JSON. Check the attachment table's actual SQL aliases.
        foreach (var command in queries.Commands)
        {
            foreach (Match alias in Regex.Matches(command.Sql, @"\b(?:FROM|JOIN) \[CommentaryAttachments\] AS (\[[^\]]+\])"))
            {
                Assert.DoesNotContain(alias.Groups[1].Value + ".[Content]", command.Sql, StringComparison.Ordinal);
            }
        }

        using HttpResponseMessage downloaded = await client.GetAsync(AttachmentPath(root, 1), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, downloaded.StatusCode);
        Assert.Equal("text/plain", downloaded.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", downloaded.Content.Headers.ContentType?.CharSet);
        Assert.Equal("attachment", downloaded.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("notes.txt", downloaded.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Equal("nosniff", Assert.Single(downloaded.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal(text, await downloaded.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        byte[] png = await client.GetByteArrayAsync(AttachmentPath(root, 0), TestContext.Current.CancellationToken);
        using MagickImage decoded = new(png);
        Assert.Equal(320u, decoded.Width);
        Assert.Equal(160u, decoded.Height);

        using HttpResponseMessage wrongOwner = await client.GetAsync($"/api/comments/{reply.Id}/attachments/{picture.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, wrongOwner.StatusCode);
        using HttpResponseMessage missing = await client.GetAsync($"/api/comments/{root.Id}/attachments/{Guid.NewGuid()}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Theory]
    [InlineData("bad.png", "image/png", "pretending to be a PNG")]
    [InlineData("bad.txt", "text/plain", "binary\0payload")]
    [InlineData("bad.gif", "image/gif", "GIF89a")]
    public async Task Attachments_InvalidFileRejectsEntirePublication(string name, string mime, string content)
    {
        using MultipartFormDataContent form = CommentForm();
        AddFile(form, "good.txt", "valid"u8.ToArray());
        AddFile(form, name, Encoding.UTF8.GetBytes(content), mime);
        using HttpResponseMessage response = await client.PostAsync("/api/comments", form, TestContext.Current.CancellationToken);
        ValidationProblemDetails problem = await ReadValidationProblemAsync(response);
        Assert.Contains("attachments[1]", problem.Errors.Keys);
        await AssertNoPublication();
    }

    [Fact]
    public async Task Attachments_RejectsElevenFilesAndAcceptsTen()
    {
        using MultipartFormDataContent invalid = CommentForm();
        for (int index = 0; index < 11; index++) AddFile(invalid, $"file-{index}.txt", "text"u8.ToArray());
        using HttpResponseMessage response = await client.PostAsync("/api/comments", invalid, TestContext.Current.CancellationToken);
        Assert.Contains("attachments", (await ReadValidationProblemAsync(response)).Errors.Keys);
        await AssertNoPublication();

        using MultipartFormDataContent valid = CommentForm();
        for (int index = 0; index < 10; index++) AddFile(valid, $"file-{index}.txt", "text"u8.ToArray());
        Assert.Equal(10, (await PostForm(valid)).Attachments.Count);
    }

    [Theory]
    [InlineData("large.jpg", 2097153)]
    [InlineData("large.txt", 102401)]
    [InlineData("empty.txt", 0)]
    public async Task Attachments_RejectsSizeViolations(string name, int size)
    {
        using MultipartFormDataContent form = CommentForm();
        AddFile(form, name, new byte[size]);
        using HttpResponseMessage response = await client.PostAsync("/api/comments", form, TestContext.Current.CancellationToken);
        Assert.Contains("attachments[0]", (await ReadValidationProblemAsync(response)).Errors.Keys);
        await AssertNoPublication();
    }

    [Fact]
    public async Task Attachments_MultipartValidatesCommentParentAndUnknownFileFields()
    {
        using MultipartFormDataContent badText = new();
        badText.Add(new StringContent("Bad user!"), "username");
        badText.Add(new StringContent("test-token"), "captchaToken");
        badText.Add(new StringContent("invalid"), "email");
        badText.Add(new StringContent(" "), "content");
        AddFile(badText, "valid.txt", "text"u8.ToArray());
        using HttpResponseMessage invalidText = await client.PostAsync("/api/comments", badText, TestContext.Current.CancellationToken);
        ValidationProblemDetails problem = await ReadValidationProblemAsync(invalidText);
        Assert.Equal(new[] { "content", "email", "username" }, problem.Errors.Keys.Order());

        using MultipartFormDataContent badParent = CommentForm(Guid.NewGuid());
        AddFile(badParent, "valid.txt", "text"u8.ToArray());
        using HttpResponseMessage invalidParent = await client.PostAsync("/api/comments", badParent, TestContext.Current.CancellationToken);
        Assert.Contains("parentId", (await ReadValidationProblemAsync(invalidParent)).Errors.Keys);

        using MultipartFormDataContent unknownFile = CommentForm();
        unknownFile.Add(new ByteArrayContent("text"u8.ToArray()), "unexpected", "file.txt");
        using HttpResponseMessage invalidField = await client.PostAsync("/api/comments", unknownFile, TestContext.Current.CancellationToken);
        Assert.Contains("attachments", (await ReadValidationProblemAsync(invalidField)).Errors.Keys);
        await AssertNoPublication();
    }

    [Fact]
    public async Task Attachments_DatabaseFailureRollsBackCommentAndAllFiles()
    {
        await using (ThreadlyDbContext context = CreateContext())
        {
            Commentary comment = new("Reader", "reader@example.com", [new TextContentBlock("Atomic insert")], DateTime.UtcNow);
            comment.AddAttachment("first.txt", "text/plain", "first"u8.ToArray(), null, null);
            comment.AddAttachment("second.txt", "text/plain", "second"u8.ToArray(), null, null);
            context.Add(comment);
            context.Entry(comment.Attachments.Last()).Property(value => value.Position).CurrentValue = 10;
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
        }
        await AssertNoPublication();
    }

    [Fact]
    public async Task Attachments_SurviveApplicationRestartAndAreRemovedWithTheirComment()
    {
        using MultipartFormDataContent form = CommentForm();
        AddFile(form, "durable.txt", "stored in SQL"u8.ToArray());
        CommentaryDto created = await PostForm(form);
        client.Dispose();
        await factory.DisposeAsync();
        await InitializeAsync();
        Assert.Equal("stored in SQL", await client.GetStringAsync(AttachmentPath(created, 0), TestContext.Current.CancellationToken));
        CommentaryDto reopened = (await client.GetFromJsonAsync<CommentaryDto>($"/api/comments/{created.Id}", TestContext.Current.CancellationToken))!;
        Assert.Equivalent(created, reopened);
        await using ThreadlyDbContext context = CreateContext();
        await context.Commentaries.Where(value => value.Id == created.Id).ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        Assert.Empty(await context.CommentaryAttachments.ToListAsync(TestContext.Current.CancellationToken));
    }

    private static MultipartFormDataContent CommentForm(Guid? parent = null)
    {
        MultipartFormDataContent form = new();
        form.Add(new StringContent("test-token"), "captchaToken");
        form.Add(new StringContent("Reader"), "username");
        form.Add(new StringContent("reader@example.com"), "email");
        form.Add(new StringContent("[{\"type\":\"text\",\"html\":\"<i>Comment with files 🧵</i>\"}]"), "content");
        if (parent is not null) form.Add(new StringContent(parent.Value.ToString()), "parentId");
        return form;
    }

    private static void AddFile(MultipartFormDataContent form, string name, byte[] bytes, string mime = "text/plain")
    {
        ByteArrayContent file = new(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(mime);
        form.Add(file, "attachments", name);
    }

    private async Task<CommentaryDto> PostForm(MultipartFormDataContent form)
    {
        using HttpResponseMessage response = await client.PostAsync("/api/comments", form, TestContext.Current.CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.NotNull(response.Headers.Location);
        return (await response.Content.ReadFromJsonAsync<CommentaryDto>(TestContext.Current.CancellationToken))!;
    }

    private static string AttachmentPath(CommentaryDto comment, int index) => $"/api/comments/{comment.Id}/attachments/{comment.Attachments[index].Id}";

    private async Task AssertNoPublication()
    {
        await using ThreadlyDbContext context = CreateContext();
        Assert.Empty(await context.Commentaries.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.CommentaryAttachments.ToListAsync(TestContext.Current.CancellationToken));
    }
}
