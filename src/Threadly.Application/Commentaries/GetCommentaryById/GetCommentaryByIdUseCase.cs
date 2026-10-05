namespace Threadly.Application.Commentaries.GetCommentaryById;

public sealed class GetCommentaryByIdUseCase(ICommentaryRepository repository)
{
    public Task<CommentaryDto?> ExecuteAsync(Guid id, CancellationToken cancellationToken)
    {
        return repository.GetByIdAsync(id, cancellationToken);
    }
}
