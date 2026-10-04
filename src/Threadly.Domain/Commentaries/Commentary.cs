namespace Threadly.Domain.Commentaries;

public sealed class Commentary
{
    public const int MaxUsernameLength = 32;
    public const int MaxEmailLength = 254;
    public const int MaxTextLength = 2000;

    public Guid Id { get; private set; }
    public string Username { get; private set; }
    public string Email { get; private set; }
    public string Text { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    private Commentary()
    {
        Username = string.Empty;
        Email = string.Empty;
        Text = string.Empty;
    }

    public Commentary(string username, string email, string text, DateTime createdAtUtc)
    {
        Id = Guid.CreateVersion7();
        Username = Validation.RequireUsername(username, MaxUsernameLength, nameof(username));
        Email = Validation.RequireEmail(email, MaxEmailLength, nameof(email));
        Text = Validation.RequireCommentary(text, MaxTextLength, nameof(text));
        CreatedAtUtc = Validation.RequireUtc(createdAtUtc, nameof(createdAtUtc));
    }
}
