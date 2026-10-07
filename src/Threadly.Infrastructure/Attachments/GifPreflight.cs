using System.Buffers.Binary;
using Threadly.Application.Commentaries;
using Threadly.Application.Commentaries.Attachments;

namespace Threadly.Infrastructure.Attachments;

internal static class GifPreflight
{
    public static void Validate(ReadOnlySpan<byte> data, string field)
    {
        // Image decoders may tolerate a missing trailer. Check the complete block structure,
        // frame count and logical canvas before handing any GIF to the native decoder.
        Require(data, 0, 13, field);
        long width = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
        long height = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]);
        int offset = 13;
        SkipColorTable(data, ref offset, data[10], field);
        int frames = 0;
        while (true)
        {
            Require(data, offset, 1, field);
            switch (data[offset++])
            {
                case 0x21:
                    Require(data, offset, 1, field);
                    offset++; // Extension label; its data uses the same bounded sub-block structure.
                    SkipBlocks(data, ref offset, field);
                    break;
                case 0x2c:
                    Require(data, offset, 9, field);
                    int left = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
                    int top = BinaryPrimitives.ReadUInt16LittleEndian(data[(offset + 2)..]);
                    int frameWidth = BinaryPrimitives.ReadUInt16LittleEndian(data[(offset + 4)..]);
                    int frameHeight = BinaryPrimitives.ReadUInt16LittleEndian(data[(offset + 6)..]);
                    if (frameWidth == 0 || frameHeight == 0 || width == 0 || height == 0) throw Invalid(field);
                    width = Math.Max(width, left + frameWidth);
                    height = Math.Max(height, top + frameHeight);
                    frames++;
                    if (frames > AttachmentLimits.MaxFrames || width > AttachmentLimits.MaxDimension
                        || height > AttachmentLimits.MaxDimension || width * height * frames > AttachmentLimits.MaxDecodedPixels)
                    {
                        throw new CommentaryValidationException(field,
                            "GIF is limited to 100 frames, 8192 px per side and 16 million decoded pixels across all frames.");
                    }
                    byte packed = data[offset + 8];
                    offset += 9;
                    SkipColorTable(data, ref offset, packed, field);
                    Require(data, offset, 1, field);
                    if (data[offset++] is < 2 or > 8) throw Invalid(field);
                    SkipBlocks(data, ref offset, field);
                    break;
                case 0x3b:
                    if (frames == 0 || offset != data.Length) throw Invalid(field);
                    return;
                default:
                    throw Invalid(field);
            }
        }
    }

    private static void SkipColorTable(ReadOnlySpan<byte> data, ref int offset, byte packed, string field)
    {
        int size = (packed & 0x80) == 0 ? 0 : 3 * (1 << ((packed & 7) + 1));
        Require(data, offset, size, field);
        offset += size;
    }

    private static void SkipBlocks(ReadOnlySpan<byte> data, ref int offset, string field)
    {
        while (true)
        {
            Require(data, offset, 1, field);
            int size = data[offset++];
            if (size == 0) return;
            Require(data, offset, size, field);
            offset += size;
        }
    }

    private static void Require(ReadOnlySpan<byte> data, int offset, int count, string field)
    {
        if (offset + count > data.Length) throw Invalid(field);
    }

    private static CommentaryValidationException Invalid(string field) => new(field, "The GIF is damaged or incomplete.");
}
