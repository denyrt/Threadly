namespace Threadly.Application.Commentaries;

public sealed record CommentCreated(Guid EventId, Guid CommentId, Guid? ParentId, DateTime CreatedAtUtc);

public interface ICommentCreatedPublisher
{
    Task PublishAsync(CommentCreated notification, CancellationToken cancellationToken);
}
