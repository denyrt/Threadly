using Threadly.Domain.Commentaries;

namespace Threadly.Application.Commentaries;

public sealed record CommentaryDto(Guid Id, string Username, string Email, string Text, DateTime CreatedAtUtc)
{
    public static CommentaryDto From(Commentary commentary)
    {
        return new CommentaryDto(
            commentary.Id, commentary.Username, commentary.Email, commentary.Text, commentary.CreatedAtUtc);
    }
}
