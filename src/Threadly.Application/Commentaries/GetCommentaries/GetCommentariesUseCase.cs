namespace Threadly.Application.Commentaries.GetCommentaries;

public sealed class GetCommentariesUseCase(ICommentaryRepository repository)
{
    public const int PageSize = 25;

    public Task<CommentaryPage> ExecuteAsync(GetCommentariesInput input, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(input.Page, 1);

        return repository.GetPageAsync(input.Page, PageSize, input.SortBy, input.SortDirection, cancellationToken);
    }
}
