namespace Threadly.Application.Commentaries.GetCommentaryReplies;

public sealed class GetCommentaryRepliesUseCase(ICommentaryRepository repository)
{
    public const int PageSize = 10;

    public async Task<CommentaryReplies?> ExecuteAsync(Guid id, string? cursor, CancellationToken cancellationToken)
    {
        ReplyCursor? position = cursor is null ? null : ReplyCursor.Decode(cursor, id);
        if (!await repository.ExistsAsync(id, cancellationToken))
        {
            return null;
        }

        return await repository.GetRepliesAsync(id, position, PageSize, cancellationToken);
    }
}
