using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Data.SqlTypes;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Threadly.Application.Commentaries;
using Threadly.Domain.Commentaries;
using Threadly.Infrastructure.Persistence;
using Xunit;

namespace Threadly.Api.IntegrationTests.Commentaries;

public sealed partial class CommentsApiTests
{
    [Fact]
    public async Task Replies_CanReplyToRepliesAndCountOnlyDirectChildrenWithoutChangingRootPages()
    {
        CommentaryDto root = await PostCommentAsync(null);
        CommentaryDto otherRoot = await PostCommentAsync(null);
        CommentaryPage before = await ReadPageAsync("/api/comments");
        CommentaryDto reply = await PostCommentAsync(root.Id);
        CommentaryDto sibling = await PostCommentAsync(root.Id);
        CommentaryDto grandchild = await PostCommentAsync(reply.Id);

        CommentaryPage after = await ReadPageAsync("/api/comments");
        Assert.Equal(before.TotalCount, after.TotalCount);
        Assert.Equal(before.Items.Select(item => item.Id), after.Items.Select(item => item.Id));
        Assert.All(after.Items, item => Assert.Null(item.ParentId));
        Assert.Equal(2, after.Items.Single(item => item.Id == root.Id).ReplyCount);
        Assert.Equal(0, after.Items.Single(item => item.Id == otherRoot.Id).ReplyCount);

        CommentaryDto readReply = (await client.GetFromJsonAsync<CommentaryDto>(
            $"/api/comments/{reply.Id}", TestContext.Current.CancellationToken))!;
        Assert.Equal(root.Id, readReply.ParentId);
        Assert.Equal(1, readReply.ReplyCount);
        CommentaryReplies children = await ReadRepliesAsync(root.Id);
        Assert.Equal(new[] { reply.Id, sibling.Id }, children.Items.Select(item => item.Id));
        Assert.Equal(1, children.Items[0].ReplyCount);
        Assert.Null(children.NextCursor);
        Assert.Equivalent(grandchild, Assert.Single((await ReadRepliesAsync(reply.Id)).Items));
    }

    [Theory]
    [InlineData("\"not-a-guid\"")]
    [InlineData("\"\"")]
    [InlineData("\"00000000-0000-0000-0000-000000000000\"")]
    [InlineData("\"11111111-1111-1111-1111-111111111111\"")]
    [InlineData("123")]
    public async Task Create_InvalidParentIsAFieldErrorAndDoesNotSave(string parentJson)
    {
        string json = "{\"captchaToken\":\"test-token\",\"username\":\"Reader\",\"email\":\"reader@example.com\",\"content\":[{\"type\":\"text\",\"html\":\"A reply\"}],\"parentId\":" + parentJson + "}";
        using StringContent content = new(json, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await client.PostAsync("/api/comments", content, TestContext.Current.CancellationToken);
        ValidationProblemDetails problem = await ReadValidationProblemAsync(response);
        Assert.Contains("parentId", problem.Errors.Keys);
        await using ThreadlyDbContext context = CreateContext();
        Assert.Equal(0, await context.Commentaries.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetReplies_DistinguishesMissingParentFromEmptyBranch()
    {
        using HttpResponseMessage missing = await client.GetAsync($"/api/comments/{Guid.NewGuid()}/replies", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        CommentaryDto root = await PostCommentAsync(null);
        CommentaryReplies empty = await ReadRepliesAsync(root.Id);
        Assert.Empty(empty.Items);
        Assert.Null(empty.NextCursor);
    }

    [Fact]
    public async Task GetReplies_UsesSqlGuidOrderAndFullTimePrecisionAcrossBoundedPages()
    {
        CommentaryDto root = await PostCommentAsync(null);
        CommentaryDto other = await PostCommentAsync(null);
        DateTime timestamp = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc).AddTicks(1234567);
        List<(Guid Id, DateTime Time)> expected = [];
        await using (ThreadlyDbContext context = CreateContext())
        {
            for (int index = 0; index < 36; index++)
            {
                // Reverse the high bytes relative to the low bytes to expose .NET vs SQL GUID ordering.
                Guid id = Guid.Parse($"{100 - index:x8}-0000-7000-8000-{index:x12}");
                DateTime time = timestamp.AddTicks(index / 24);
                Commentary reply = new("Reader", "reader@example.com", [new TextContentBlock($"Reply {index}")], time, root.Id);
                context.Add(reply);
                context.Entry(reply).Property(value => value.Id).CurrentValue = id;
                expected.Add((id, time));
            }
            context.Add(new Commentary("Reader", "reader@example.com", [new TextContentBlock("Different parent")], timestamp, other.Id));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        Guid[] ordered = expected.OrderBy(value => value.Time).ThenBy(value => new SqlGuid(value.Id)).Select(value => value.Id).ToArray();
        Assert.NotEqual(expected.OrderBy(value => value.Time).ThenBy(value => value.Id).Select(value => value.Id), ordered);
        List<Guid> actual = [];
        string? cursor = null;
        for (int batch = 0; batch < 4; batch++)
        {
            CommentaryReplies page = await ReadRepliesAsync(root.Id, cursor);
            Assert.Equal(ordered.Skip(batch * 10).Take(10), page.Items.Select(value => value.Id));
            Assert.All(page.Items, item => Assert.Equal(expected.Single(value => value.Id == item.Id).Time, item.CreatedAtUtc));
            actual.AddRange(page.Items.Select(item => item.Id));
            cursor = page.NextCursor;
            if (batch < 3) Assert.NotNull(cursor);
        }
        Assert.Null(cursor);
        Assert.Equal(ordered, actual);
        Assert.Equal(actual.Count, actual.Distinct().Count());
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("%%%")]
    public async Task GetReplies_RejectsMalformedCursor(string cursor)
    {
        CommentaryDto root = await PostCommentAsync(null);
        using HttpResponseMessage response = await client.GetAsync(
            $"/api/comments/{root.Id}/replies?cursor={Uri.EscapeDataString(cursor)}", TestContext.Current.CancellationToken);
        Assert.Contains("cursor", (await ReadValidationProblemAsync(response)).Errors.Keys);
    }

    [Fact]
    public async Task GetReplies_RejectsCursorFromAnotherParentOrUnknownPosition()
    {
        CommentaryDto root = await PostCommentAsync(null);
        CommentaryDto other = await PostCommentAsync(null);
        for (int index = 0; index < 11; index++) await PostCommentAsync(root.Id);
        string cursor = (await ReadRepliesAsync(root.Id)).NextCursor!;
        foreach (string path in new[]
        {
            $"/api/comments/{other.Id}/replies?cursor={cursor}",
            $"/api/comments/{root.Id}/replies?cursor={new ReplyCursor(root.Id, DateTime.UtcNow, Guid.NewGuid()).Encode()}"
        })
        {
            using HttpResponseMessage response = await client.GetAsync(path, TestContext.Current.CancellationToken);
            Assert.Contains("cursor", (await ReadValidationProblemAsync(response)).Errors.Keys);
        }
    }

    [Fact]
    public async Task Repository_MapsParentForeignKeyFailureToValidation()
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ICommentaryRepository repository = scope.ServiceProvider.GetRequiredService<ICommentaryRepository>();
        Commentary reply = new("Reader", "reader@example.com", [new TextContentBlock("Reply")], DateTime.UtcNow, Guid.NewGuid());
        CommentaryValidationException exception = await Assert.ThrowsAsync<CommentaryValidationException>(
            () => repository.AddAsync(reply, TestContext.Current.CancellationToken));
        Assert.Equal("parentId", exception.Field);
        await using ThreadlyDbContext context = CreateContext();
        Assert.Empty(await context.Commentaries.ToListAsync(TestContext.Current.CancellationToken));
    }

    private async Task<CommentaryDto> PostCommentAsync(Guid? parentId)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/comments", new
        {
            captchaToken = "test-token",
            username = "Reader",
            email = "reader@example.com",
            content = new[] { new { type = "text", html = "<strong>A comment</strong>" } },
            parentId
        }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        CommentaryDto result = (await response.Content.ReadFromJsonAsync<CommentaryDto>(TestContext.Current.CancellationToken))!;
        Assert.Equal(parentId, result.ParentId);
        Assert.Equal(0, result.ReplyCount);
        Assert.EndsWith($"/api/comments/{result.Id}", response.Headers.Location!.ToString());
        return result;
    }

    private async Task<CommentaryReplies> ReadRepliesAsync(Guid id, string? cursor = null)
    {
        string path = $"/api/comments/{id}/replies" + (cursor is null ? "" : $"?cursor={Uri.EscapeDataString(cursor)}");
        using HttpResponseMessage response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CommentaryReplies>(TestContext.Current.CancellationToken))!;
    }

    [Fact]
    public async Task Reads_KeepCountsInSqlAndLimitReplyQueriesBeforeMaterializing()
    {
        CommentaryDto root = await PostCommentAsync(null);
        for (int index = 0; index < 12; index++) await PostCommentAsync(root.Id);
        queries.Commands.Clear();
        CommentaryPage page = await ReadPageAsync("/api/comments");
        Assert.Equal(12, Assert.Single(page.Items).ReplyCount);
        Assert.Equal(2, queries.Commands.Count);

        queries.Commands.Clear();
        CommentaryReplies first = await ReadRepliesAsync(root.Id);
        Assert.Equal(2, queries.Commands.Count);
        Assert.Equal(10, first.Items.Count);
        Assert.Contains("TOP(", queries.Commands[^1].Sql);
        Assert.Contains(11, queries.Commands[^1].Parameters);

        queries.Commands.Clear();
        CommentaryReplies last = await ReadRepliesAsync(root.Id, first.NextCursor);
        Assert.Equal(3, queries.Commands.Count);
        Assert.Equal(2, last.Items.Count);
        Assert.Null(last.NextCursor);
        Assert.Contains("TOP(", queries.Commands[^1].Sql);
        Assert.Contains(11, queries.Commands[^1].Parameters);
    }
}
