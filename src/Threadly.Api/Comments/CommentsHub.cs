using Microsoft.AspNetCore.SignalR;
using Threadly.Application.Commentaries;

namespace Threadly.Api.Comments;

public sealed class CommentsHub : Hub
{
    public const int MaxSubscriptions = 100;
    public const string RootsGroup = "comments:roots";
    private const string SubscriptionsKey = "comments:subscriptions";

    public static string RepliesGroup(Guid parentId) => $"comments:replies:{parentId:D}";

    // Full replacement is idempotent. Hub invocations are serialized per connection.
    public async Task SetSubscriptions(bool roots, Guid[]? parentIds)
    {
        if (parentIds is null || parentIds.Length > MaxSubscriptions || parentIds.Contains(Guid.Empty))
            throw new HubException($"Supply at most {MaxSubscriptions} non-empty comment IDs.");

        HashSet<string> desired = parentIds.Select(RepliesGroup).ToHashSet(StringComparer.Ordinal);
        if (roots) desired.Add(RootsGroup);
        HashSet<string> previous = Context.Items.TryGetValue(SubscriptionsKey, out object? value)
            ? (HashSet<string>)value! : [];
        try
        {
            foreach (string group in previous.Except(desired))
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, group, Context.ConnectionAborted);
            foreach (string group in desired.Except(previous))
                await Groups.AddToGroupAsync(Context.ConnectionId, group, Context.ConnectionAborted);
            Context.Items[SubscriptionsKey] = desired;
        }
        catch
        {
            // Never retain a connection with an uncertain partial subscription set.
            Context.Abort();
            throw;
        }
    }
}

public sealed class SignalRCommentCreatedPublisher(IHubContext<CommentsHub> hub) : ICommentCreatedPublisher
{
    public Task PublishAsync(CommentCreated notification, CancellationToken cancellationToken) =>
        hub.Clients.Group(notification.ParentId is Guid parentId
                ? CommentsHub.RepliesGroup(parentId) : CommentsHub.RootsGroup)
            .SendAsync("CommentCreated", notification, cancellationToken);
}
