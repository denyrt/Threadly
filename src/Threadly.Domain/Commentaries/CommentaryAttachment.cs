namespace Threadly.Domain.Commentaries;

public sealed class CommentaryAttachment
{
    public const int MaxFileNameLength = 128;
    public const int MaxContentBytes = 2 * 1024 * 1024;

    public Guid Id { get; private set; }
    public Guid CommentaryId { get; private set; }
    public int Position { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public int Size { get; private set; }
    public int? Width { get; private set; }
    public int? Height { get; private set; }
    public byte[] Content { get; private set; } = [];

    private CommentaryAttachment() { }

    internal CommentaryAttachment(Guid commentaryId, int position, string fileName, string contentType,
        byte[] content, int? width, int? height)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(content);
        if (fileName.Length > MaxFileNameLength || content.Length is 0 or > MaxContentBytes)
        {
            throw new ArgumentException("Attachment name or content exceeds its limits.");
        }

        bool image = contentType is "image/jpeg" or "image/png" or "image/gif";
        if ((!image && contentType != "text/plain")
            || (image && (width is not (>= 1 and <= 320) || height is not (>= 1 and <= 240)))
            || (!image && (width is not null || height is not null || content.Length > 100 * 1024)))
        {
            throw new ArgumentException("Invalid attachment type or dimensions.");
        }

        Id = Guid.CreateVersion7();
        CommentaryId = commentaryId;
        Position = position;
        FileName = fileName;
        ContentType = contentType;
        Content = content;
        Size = content.Length;
        Width = width;
        Height = height;
    }
}
