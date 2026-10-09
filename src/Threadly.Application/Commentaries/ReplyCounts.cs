namespace Threadly.Application.Commentaries;

public sealed record ReplyCountDto(Guid Id, int ReplyCount);
public sealed record ReplyCounts(IReadOnlyList<ReplyCountDto> Items);

public sealed class GetReplyCountsUseCase(ICommentaryRepository repository)
{
    public const int MaxIds = 100;

    public async Task<ReplyCounts> ExecuteAsync(IReadOnlyList<Guid>? ids, CancellationToken cancellationToken)
    {
        if (ids is null || ids.Count > MaxIds || ids.Contains(Guid.Empty))
            throw new CommentaryValidationException("ids", $"Supply at most {MaxIds} non-empty comment IDs.");

        // Missing comments are omitted; existing comments without children return zero.
        return new(await repository.GetReplyCountsAsync(ids.Distinct().ToArray(), cancellationToken));
    }
}
