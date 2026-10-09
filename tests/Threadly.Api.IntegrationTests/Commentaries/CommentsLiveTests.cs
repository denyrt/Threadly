using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Threadly.Application.Commentaries;
using Xunit;

namespace Threadly.Api.IntegrationTests.Commentaries;

public sealed partial class CommentsApiTests
{
    private HubConnection LiveConnection() => new HubConnectionBuilder()
        .WithUrl("https://localhost/hubs/comments", options =>
        {
            options.Transports = HttpTransportType.WebSockets;
            options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
            options.WebSocketFactory = async (context, token) =>
                await factory.Server.CreateWebSocketClient().ConnectAsync(context.Uri, token);
        }).Build();

    [Fact]
    public async Task LiveHub_RoutesCommittedJsonAndMultipartCreatesToImmediateGroupsOnly()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using HubConnection roots = LiveConnection();
        await using HubConnection replies = LiveConnection();
        ConcurrentQueue<CommentCreated> rootEvents = new();
        ConcurrentQueue<CommentCreated> replyEvents = new();
        roots.On<CommentCreated>("CommentCreated", rootEvents.Enqueue);
        replies.On<CommentCreated>("CommentCreated", replyEvents.Enqueue);
        await roots.StartAsync(token);
        await replies.StartAsync(token);
        await roots.InvokeAsync("SetSubscriptions", true, Array.Empty<Guid>(), token);
        CommentaryDto root = await CreateLiveComment();
        await WaitForEvent(rootEvents, root.Id);
        Assert.Empty(replyEvents);
        await replies.InvokeAsync("SetSubscriptions", false, new[] { root.Id, root.Id }, token);
        await replies.InvokeAsync("SetSubscriptions", false, new[] { root.Id }, token);
        CommentaryDto reply = await CreateLiveComment(root.Id, multipart: true);
        await WaitForEvent(replyEvents, reply.Id);
        Assert.Equal(root.Id, Assert.Single(rootEvents).CommentId);
        CommentCreated notification = Assert.Single(replyEvents);
        Assert.Equal(reply.ParentId, notification.ParentId);
        Assert.Equal(reply.CreatedAtUtc, notification.CreatedAtUtc);
        Assert.NotEqual(Guid.Empty, notification.EventId);
        // Receiving a notification implies the row and attachment are already readable from SQL.
        CommentaryDto? stored = await client.GetFromJsonAsync<CommentaryDto>($"/api/comments/{reply.Id}", token);
        Assert.Equal("note.txt", Assert.Single(stored!.Attachments).FileName);
        string json = System.Text.Json.JsonSerializer.Serialize(notification);
        Assert.DoesNotContain("email", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("content", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("captcha", json, StringComparison.OrdinalIgnoreCase);

        await CreateLiveComment(reply.Id); // Not routed to the root's direct-reply group.
        await replies.InvokeAsync("SetSubscriptions", false, Array.Empty<Guid>(), token);
        await replies.InvokeAsync("SetSubscriptions", false, Array.Empty<Guid>(), token);
        await CreateLiveComment(root.Id);
        // A marker on the same ordered transport fences all prior messages.
        await replies.InvokeAsync("SetSubscriptions", true, Array.Empty<Guid>(), token);
        CommentaryDto marker = await CreateLiveComment(multipart: true);
        await WaitForEvent(replyEvents, marker.Id);
        Assert.Equal(new[] { reply.Id, marker.Id }, replyEvents.Select(value => value.CommentId));
        Assert.Equal(2, rootEvents.Count);
        Assert.Equal(replyEvents.Count, replyEvents.Select(value => value.EventId).Distinct().Count());
    }

    [Fact]
    public async Task LiveHub_ValidatesLimitsSerializesReplacementAndExposesNoBroadcastMethod()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using HubConnection connection = LiveConnection();
        ConcurrentQueue<CommentCreated> events = new();
        connection.On<CommentCreated>("CommentCreated", events.Enqueue);
        await connection.StartAsync(token);
        await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("SetSubscriptions", true, new[] { Guid.Empty }, token));
        await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("SetSubscriptions", true, Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray(), token));
        await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("SetSubscriptions", true, new[] { "invalid" }, token));
        await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("CommentCreated", new { commentId = Guid.NewGuid() }, token));
        await Task.WhenAll(Enumerable.Range(0, 10).Select(index => connection.InvokeAsync("SetSubscriptions", index % 2 == 0, new[] { Guid.NewGuid() }, token)));
        await connection.InvokeAsync("SetSubscriptions", true, Array.Empty<Guid>(), token);
        CommentaryDto marker = await CreateLiveComment();
        await WaitForEvent(events, marker.Id);
        Assert.Single(events);
    }

    [Fact]
    public async Task LiveHub_FailedValidationCaptchaAndAttachmentsNeverPublish()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using HubConnection connection = LiveConnection();
        ConcurrentQueue<CommentCreated> events = new();
        connection.On<CommentCreated>("CommentCreated", events.Enqueue);
        await connection.StartAsync(token);
        await connection.InvokeAsync("SetSubscriptions", true, Array.Empty<Guid>(), token);
        foreach (var payload in new[] {
            new { username = "Invalid!", captchaToken = "test-token" },
            new { username = "Reader", captchaToken = "invalid" } })
        {
            using HttpResponseMessage response = await client.PostAsJsonAsync("/api/comments", new
            {
                payload.username,
                payload.captchaToken,
                email = "reader@example.com",
                content = new[] { new { type = "text", html = "hello" } }
            }, token);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using MultipartFormDataContent invalid = LiveForm();
        invalid.Add(new ByteArrayContent([0]), "attachments", "bad.exe");
        using HttpResponseMessage failed = await client.PostAsync("/api/comments", invalid, token);
        Assert.Equal(HttpStatusCode.BadRequest, failed.StatusCode);
        CommentaryDto marker = await CreateLiveComment();
        await WaitForEvent(events, marker.Id);
        Assert.Equal(marker.Id, Assert.Single(events).CommentId);
    }

    [Fact]
    public async Task ReplyCounts_AreBoundedDeduplicatedDirectAndDoNotReadBodiesOrSpendCaptcha()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        CommentaryDto root = await CreateLiveComment();
        CommentaryDto child = await CreateLiveComment(root.Id, multipart: true);
        CommentaryDto grandchild = await CreateLiveComment(child.Id);
        int captchaCalls = captcha.Calls;
        queries.Commands.Clear();
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/comments/reply-counts",
            new { ids = new[] { root.Id, child.Id, grandchild.Id, root.Id, Guid.NewGuid() } }, token);
        response.EnsureSuccessStatusCode();
        ReplyCounts counts = (await response.Content.ReadFromJsonAsync<ReplyCounts>(token))!;
        Assert.Equal(3, counts.Items.Count);
        Assert.Equal(1, counts.Items.Single(value => value.Id == root.Id).ReplyCount);
        Assert.Equal(1, counts.Items.Single(value => value.Id == child.Id).ReplyCount);
        Assert.Equal(0, counts.Items.Single(value => value.Id == grandchild.Id).ReplyCount);
        string sql = Assert.Single(queries.Commands).Sql;
        Assert.DoesNotContain("Content", sql);
        Assert.DoesNotContain("Attachment", sql);
        Assert.DoesNotContain("Email", sql);
        Assert.Equal(captchaCalls, captcha.Calls);
        foreach (Guid[] ids in new[] { new[] { Guid.Empty }, Enumerable.Repeat(root.Id, 101).ToArray() })
        {
            using HttpResponseMessage invalid = await client.PostAsJsonAsync("/api/comments/reply-counts", new { ids }, token);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        using HttpResponseMessage empty = await client.PostAsJsonAsync("/api/comments/reply-counts", new { ids = Array.Empty<Guid>() }, token);
        Assert.Empty((await empty.Content.ReadFromJsonAsync<ReplyCounts>(token))!.Items);
    }

    private async Task<CommentaryDto> CreateLiveComment(Guid? parentId = null, bool multipart = false)
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using HttpContent body = multipart ? LiveForm(parentId) : JsonContent.Create(new
        {
            username = "Reader",
            email = "reader@example.com",
            captchaToken = "test-token",
            parentId,
            content = new[] { new { type = "text", html = "hello" } }
        });
        if (multipart) ((MultipartFormDataContent)body).Add(new ByteArrayContent(Encoding.UTF8.GetBytes("hello")), "attachments", "note.txt");
        using HttpResponseMessage response = await client.PostAsync("/api/comments", body, token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CommentaryDto>(token))!;
    }

    private static MultipartFormDataContent LiveForm(Guid? parentId = null)
    {
        MultipartFormDataContent form = new();
        form.Add(new StringContent("Reader"), "username");
        form.Add(new StringContent("reader@example.com"), "email");
        form.Add(new StringContent("test-token"), "captchaToken");
        form.Add(new StringContent("[{\"type\":\"text\",\"html\":\"hello\"}]"), "content");
        if (parentId.HasValue) form.Add(new StringContent(parentId.Value.ToString()), "parentId");
        return form;
    }

    private static async Task WaitForEvent(ConcurrentQueue<CommentCreated> events, Guid id)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        while (!events.Any(value => value.CommentId == id)) await Task.Delay(10, timeout.Token);
    }
}
