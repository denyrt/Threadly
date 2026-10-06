namespace Threadly.Application.Commentaries;

public sealed class CommentaryValidationException(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}
