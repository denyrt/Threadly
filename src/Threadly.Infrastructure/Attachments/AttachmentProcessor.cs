using ImageMagick;
using System.Text;
using Threadly.Application.Commentaries;
using Threadly.Application.Commentaries.Attachments;
using Threadly.Domain.Commentaries;

namespace Threadly.Infrastructure.Attachments;

public sealed class AttachmentProcessor : IAttachmentProcessor
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly SemaphoreSlim ImageSlots = new(2, 2);

    static AttachmentProcessor()
    {
        // Native pixel caches have process-wide limits; never spill untrusted images to disk.
        ResourceLimits.Memory = 256 * 1024 * 1024;
        ResourceLimits.Disk = 0;
        ResourceLimits.MaxMemoryRequest = 64 * 1024 * 1024;
        ResourceLimits.MaxProfileSize = AttachmentLimits.MaxImageBytes;
        ResourceLimits.Width = AttachmentLimits.MaxDimension;
        ResourceLimits.Height = AttachmentLimits.MaxDimension;
        ResourceLimits.ListLength = AttachmentLimits.MaxFrames + 1;
        ResourceLimits.Thread = 1;
    }

    public async Task<IReadOnlyList<ProcessedAttachment>> ProcessAsync(
        IReadOnlyList<AttachmentUpload> uploads, CancellationToken cancellationToken)
    {
        if (uploads.Count > Commentary.MaxAttachments)
        {
            throw new CommentaryValidationException("attachments", "Choose at most 10 attachments.");
        }

        List<ProcessedAttachment> result = [];
        for (int index = 0; index < uploads.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AttachmentUpload upload = uploads[index];
            string field = $"attachments[{index}]";
            string name = SafeFileName(upload.FileName, field);
            string extension = Path.GetExtension(name).ToLowerInvariant();
            int maxBytes = extension == ".txt" ? AttachmentLimits.MaxTextBytes : AttachmentLimits.MaxImageBytes;
            if (extension is not (".txt" or ".jpg" or ".jpeg" or ".png" or ".gif"))
            {
                throw Invalid(field, "Use JPG, PNG, GIF or TXT files.");
            }
            if (upload.Length <= 0 || upload.Length > maxBytes)
            {
                throw Invalid(field, extension == ".txt" ? "TXT must contain 1 byte to 100 KiB." : "Images must contain 1 byte to 2 MiB.");
            }

            byte[] bytes = await ReadBoundedAsync(upload, maxBytes, field, cancellationToken);
            if (extension == ".txt")
            {
                ValidateText(bytes, field);
                result.Add(new ProcessedAttachment(name, "text/plain", bytes, null, null));
                continue;
            }

            await ImageSlots.WaitAsync(cancellationToken);
            try
            {
                result.Add(ProcessImage(name, extension, bytes, field, cancellationToken));
            }
            finally
            {
                ImageSlots.Release();
            }
        }

        return result;
    }

    private static async Task<byte[]> ReadBoundedAsync(
        AttachmentUpload upload, int limit, string field, CancellationToken cancellationToken)
    {
        await using Stream source = upload.OpenReadStream();
        using MemoryStream destination = new();
        byte[] buffer = new byte[8192];
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (destination.Length + read > limit)
            {
                throw Invalid(field, "The file exceeds its size limit.");
            }
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        if (destination.Length == 0 || destination.Length != upload.Length)
        {
            throw Invalid(field, "The file is empty or incomplete.");
        }
        return destination.ToArray();
    }

    private static string SafeFileName(string value, string field)
    {
        // Names are display metadata only. Storage and download routing use generated IDs.
        string name = value.Replace('\\', '/').Split('/')[^1].Trim();
        if (name.Length is 0 or > CommentaryAttachment.MaxFileNameLength || name.Any(char.IsControl))
        {
            throw Invalid(field, "Use a file name of 1 to 128 characters without control characters.");
        }
        return new string(name.Select(character => char.IsLetterOrDigit(character)
            || character is '.' or '-' or '_' or ' ' ? character : '_').ToArray());
    }

    private static void ValidateText(byte[] bytes, string field)
    {
        try
        {
            string text = StrictUtf8.GetString(bytes);
            if (text.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t')))
            {
                throw Invalid(field, "TXT must contain UTF-8 text without binary control characters.");
            }
        }
        catch (DecoderFallbackException)
        {
            throw Invalid(field, "TXT must contain valid UTF-8 text.");
        }
    }

    private static ProcessedAttachment ProcessImage(
        string name, string extension, byte[] bytes, string field, CancellationToken cancellationToken)
    {
        (MagickFormat format, string contentType) = extension switch
        {
            ".jpg" or ".jpeg" when bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xd8, 0xff }) => (MagickFormat.Jpeg, "image/jpeg"),
            ".png" when bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) => (MagickFormat.Png, "image/png"),
            ".gif" when bytes.AsSpan().StartsWith("GIF87a"u8) || bytes.AsSpan().StartsWith("GIF89a"u8) => (MagickFormat.Gif, "image/gif"),
            _ => throw Invalid(field, "The image content does not match its file extension.")
        };

        if (format == MagickFormat.Gif)
        {
            GifPreflight.Validate(bytes, field);
        }

        try
        {
            // Force the allowlisted decoder. A signature alone does not prove that an image is valid.
            MagickReadSettings settings = new() { Format = format };
            using MagickImageCollection frames = new();
            bool warning = false;
            frames.Warning += (_, _) => warning = true;
            frames.Ping(bytes, settings);
            ValidateImageBounds(frames, field);
            if (warning)
            {
                throw Invalid(field, "The image is damaged or incomplete.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            frames.Read(bytes, settings);
            ValidateImageBounds(frames, field);
            if (warning)
            {
                throw Invalid(field, "The image is damaged or incomplete.");
            }
            if (format == MagickFormat.Gif)
            {
                frames.Coalesce();
            }
            foreach (IMagickImage<byte> frame in frames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                frame.AutoOrient();
                frame.Resize(new MagickGeometry(320, 240) { Greater = true });
                frame.ResetPage();
                frame.Strip();
                frame.Quality = 85;
            }

            byte[] normalized = frames.ToByteArray(format);
            if (normalized.Length > AttachmentLimits.MaxImageBytes)
            {
                throw Invalid(field, "The processed image exceeds 2 MiB. Use a smaller image or a shorter animation.");
            }
            return new ProcessedAttachment(name, contentType, normalized, (int)frames[0].Width, (int)frames[0].Height);
        }
        catch (MagickException)
        {
            throw Invalid(field, "The image is invalid or exceeds processing limits (8192 px per side, 100 frames, 16 million decoded pixels).");
        }
    }

    private static void ValidateImageBounds(MagickImageCollection frames, string field)
    {
        if (frames.Count is 0 or > AttachmentLimits.MaxFrames)
        {
            throw Invalid(field, "An image may contain at most 100 frames.");
        }

        long width = 0;
        long height = 0;
        foreach (IMagickImage<byte> frame in frames)
        {
            // GIF logical canvas and offsets matter even when each encoded frame is tiny.
            width = Math.Max(width, Math.Max(frame.Page.Width, (long)frame.Width + Math.Max(0, frame.Page.X)));
            height = Math.Max(height, Math.Max(frame.Page.Height, (long)frame.Height + Math.Max(0, frame.Page.Y)));
        }
        if (width is 0 or > AttachmentLimits.MaxDimension || height is 0 or > AttachmentLimits.MaxDimension
            || width * height * frames.Count > AttachmentLimits.MaxDecodedPixels)
        {
            throw Invalid(field, "Image processing is limited to 8192 px per side and 16 million decoded pixels across all frames.");
        }
    }

    private static CommentaryValidationException Invalid(string field, string message) => new(field, message);
}
