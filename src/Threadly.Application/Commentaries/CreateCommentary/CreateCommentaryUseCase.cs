using Threadly.Domain.Commentaries;

namespace Threadly.Application.Commentaries.CreateCommentary;

public sealed class CreateCommentaryUseCase(ICommentaryRepository repository, TimeProvider timeProvider)
{
    public async Task<CommentaryDto> ExecuteAsync(CreateCommentaryInput input, CancellationToken cancellationToken)
    {
        Commentary commentary = new(input.Username, input.Email, input.Text, timeProvider.GetUtcNow().UtcDateTime);

        await repository.AddAsync(commentary, cancellationToken);

        return CommentaryDto.From(commentary);
    }
}
