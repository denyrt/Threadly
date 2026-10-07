namespace Threadly.Application.Commentaries.Attachments;

public static class AttachmentLimits
{
    public const int MaxImageBytes = 2 * 1024 * 1024;
    public const int MaxTextBytes = 100 * 1024;
    public const int MaxRequestBytes = 21 * 1024 * 1024;
    public const int MaxDimension = 8192;
    public const int MaxFrames = 100;
    public const long MaxDecodedPixels = 16_000_000;
}
