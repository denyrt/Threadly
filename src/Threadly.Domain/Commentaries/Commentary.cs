namespace Threadly.Domain.Commentaries;

public sealed class Commentary
{
    public const int MaxUsernameLength = 32;
    public const int MaxEmailLength = 254;
    public const int MaxNormalizedContentLength = 16000;
    public const int MaxAttachments = 10;

    private readonly List<CommentaryAttachment> attachments = [];
    public IReadOnlyCollection<CommentaryAttachment> Attachments => attachments.AsReadOnly();

    public Guid Id { get; private set; }
    public Guid? ParentId { get; private set; }
    public string Username { get; private set; }
    public string Email { get; private set; }
    public IReadOnlyList<TextContentBlock> Content { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    private Commentary()
    {
        Username = string.Empty;
        Email = string.Empty;
        Content = [];
    }

    public void AddAttachment(string fileName, string contentType, byte[] content, int? width, int? height)
    {
        if (attachments.Count >= MaxAttachments)
        {
            throw new ArgumentException($"A comment can have at most {MaxAttachments} attachments.");
        }

        attachments.Add(new CommentaryAttachment(Id, attachments.Count, fileName, contentType, content, width, height));
    }

    public Commentary(string username, string email, IReadOnlyList<TextContentBlock> content, DateTime createdAtUtc, Guid? parentId = null)
    {
        Id = Guid.CreateVersion7();
        if (parentId == Guid.Empty || parentId == Id)
        {
            throw new ArgumentException("Parent must be a non-empty ID different from the comment ID.", nameof(parentId));
        }

        ParentId = parentId;
        Username = Validation.RequireUsername(username, MaxUsernameLength, nameof(username));
        Email = Validation.RequireEmail(email, MaxEmailLength, nameof(email));
        ArgumentNullException.ThrowIfNull(content);
        if (content.Count == 0 || content.Any(block => block is null || string.IsNullOrEmpty(block.Html))
            || content.All(block => string.IsNullOrWhiteSpace(block.Html))
            || content.Sum(block => (long)block.Html.Length) > MaxNormalizedContentLength)
        {
            throw new ArgumentException("Content must contain nonempty normalized text blocks within the storage limit.", nameof(content));
        }
        Content = Array.AsReadOnly(content.ToArray());
        CreatedAtUtc = Validation.RequireUtc(createdAtUtc, nameof(createdAtUtc));
    }
}
