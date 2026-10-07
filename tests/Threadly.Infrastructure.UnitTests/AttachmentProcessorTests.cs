using ImageMagick;
using System.Text;
using Threadly.Application.Commentaries;
using Threadly.Application.Commentaries.Attachments;
using Threadly.Infrastructure.Attachments;
using Xunit;

namespace Threadly.Infrastructure.UnitTests;

public sealed class AttachmentProcessorTests
{
    private readonly AttachmentProcessor processor = new();

    [Theory]
    [InlineData(MagickFormat.Jpeg, "photo.jpg", "image/jpeg", 640, 480, 320, 240)]
    [InlineData(MagickFormat.Png, "photo.png", "image/png", 800, 200, 320, 80)]
    [InlineData(MagickFormat.Gif, "photo.gif", "image/gif", 200, 800, 60, 240)]
    [InlineData(MagickFormat.Png, "small.PNG", "image/png", 80, 30, 80, 30)]
    public async Task Images_AreDecodedResizedWithoutUpscalingAndReencoded(
        MagickFormat format, string name, string mime, uint width, uint height, int expectedWidth, int expectedHeight)
    {
        using MagickImage source = new(MagickColors.Coral, width, height);
        source.SetAttribute("comment", "Discard this metadata");
        ProcessedAttachment attachment = await Process(name, source.ToByteArray(format));
        Assert.Equal(mime, attachment.ContentType);
        Assert.Equal(expectedWidth, attachment.Width);
        Assert.Equal(expectedHeight, attachment.Height);
        using MagickImage result = new(attachment.Content);
        Assert.Equal(format, result.Format);
        Assert.Equal((uint)expectedWidth, result.Width);
        Assert.Equal((uint)expectedHeight, result.Height);
        Assert.Null(result.GetAttribute("comment"));
    }

    [Fact]
    public async Task Gif_PreservesFramesTimingAndLoopCount()
    {
        using MagickImageCollection source = new();
        source.Add(new MagickImage(MagickColors.Red, 640, 400) { AnimationDelay = 12, AnimationIterations = 3 });
        source.Add(new MagickImage(MagickColors.Blue, 640, 400) { AnimationDelay = 24 });
        ProcessedAttachment attachment = await Process("animation.gif", source.ToByteArray(MagickFormat.Gif));
        using MagickImageCollection result = new(attachment.Content);
        Assert.Equal(2, result.Count);
        Assert.Equal(12u, result[0].AnimationDelay);
        Assert.Equal(24u, result[1].AnimationDelay);
        Assert.Equal(3u, result[0].AnimationIterations);
        Assert.All(result, frame => { Assert.Equal(320u, frame.Width); Assert.Equal(200u, frame.Height); });
    }

    [Theory]
    [InlineData("payload.jpg", "<script>alert(1)</script>")]
    [InlineData("payload.gif", "GIF89a")]
    [InlineData("payload.png", "not a PNG")]
    [InlineData("payload.svg", "<svg></svg>")]
    [InlineData("payload.jpg.php", "not an image")]
    [InlineData("binary.txt", "a\0b")]
    [InlineData("binary.txt", "a\u001bb")]
    public async Task RejectsFakeImagesUnsupportedExtensionsAndBinaryText(string name, string value)
    {
        CommentaryValidationException error = await Assert.ThrowsAsync<CommentaryValidationException>(
            () => Process(name, Encoding.UTF8.GetBytes(value)));
        Assert.Equal("attachments[0]", error.Field);
    }

    [Fact]
    public async Task RejectsInvalidUtf8AndMismatchedImageExtension()
    {
        await Assert.ThrowsAsync<CommentaryValidationException>(() => Process("binary.txt", [0xc3, 0x28]));
        using MagickImage image = new(MagickColors.Red, 10, 10);
        await Assert.ThrowsAsync<CommentaryValidationException>(() => Process("wrong.jpg", image.ToByteArray(MagickFormat.Png)));
        byte[] jpeg = image.ToByteArray(MagickFormat.Jpeg);
        await Assert.ThrowsAsync<CommentaryValidationException>(() => Process("broken.jpg", jpeg[..^20]));
    }

    [Theory]
    [InlineData(MagickFormat.Gif, "incomplete.gif")]
    [InlineData(MagickFormat.Png, "incomplete.png")]
    public async Task RejectsImagesWithMissingEndMarkers(MagickFormat format, string name)
    {
        using MagickImage image = new(MagickColors.Red, 10, 10);
        byte[] bytes = image.ToByteArray(format);
        await Assert.ThrowsAsync<CommentaryValidationException>(() => Process(name, bytes[..^1]));
    }

    [Fact]
    public async Task Text_AcceptsUtf8BomUnicodeAndExactSizeBoundary()
    {
        byte[] unicode = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("Привіт 🧵\r\n\tText")).ToArray();
        Assert.Equal(unicode, (await Process("нотатки.txt", unicode)).Content);
        byte[] boundary = Enumerable.Repeat((byte)'x', AttachmentLimits.MaxTextBytes).ToArray();
        Assert.Equal(boundary, (await Process("notes.txt", boundary)).Content);
        await Assert.ThrowsAsync<CommentaryValidationException>(() => Process("too-big.txt", [.. boundary, 120]));
        await Assert.ThrowsAsync<CommentaryValidationException>(() => Process("empty.txt", []));
    }

    [Fact]
    public async Task LimitsActualStreamBytesEvenWhenDeclaredLengthIsSmaller()
    {
        AttachmentUpload lying = new("notes.txt", 1,
            () => new MemoryStream(new byte[AttachmentLimits.MaxTextBytes + 1]));
        await Assert.ThrowsAsync<CommentaryValidationException>(() =>
            processor.ProcessAsync([lying], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CountLimitIsCheckedBeforeOpeningAnyFiles()
    {
        AttachmentUpload upload = new("notes.txt", 1, () => throw new InvalidOperationException("Must not open"));
        CommentaryValidationException error = await Assert.ThrowsAsync<CommentaryValidationException>(() =>
            processor.ProcessAsync(Enumerable.Repeat(upload, 11).ToArray(), TestContext.Current.CancellationToken));
        Assert.Equal("attachments", error.Field);
    }

    [Theory]
    [InlineData(101, 1, 1)]
    [InlineData(65, 512, 512)]
    [InlineData(1, 8193, 1)]
    [InlineData(1, 1, 8193)]
    public async Task Gif_RejectsFrameAndLogicalCanvasExpansionBeforeDecode(int count, ushort width, ushort height)
    {
        await Assert.ThrowsAsync<CommentaryValidationException>(() => Process("bomb.gif", TinyGif(count, width, height)));
    }

    [Fact]
    public async Task Gif_AcceptsOneHundredSmallFrames()
    {
        ProcessedAttachment attachment = await Process("small.gif", TinyGif(100, 1, 1));
        using MagickImageCollection image = new(attachment.Content);
        Assert.Equal(100, image.Count);
    }

    [Fact]
    public async Task FileNamesCannotControlStoragePathsOrResponseHeaders()
    {
        Assert.Equal("notes.txt", (await Process("C:\\fakepath\\notes.txt", "hello"u8.ToArray())).FileName);
        Assert.Equal("_script_.txt", (await Process("../<script>.txt", "hello"u8.ToArray())).FileName);
        await Assert.ThrowsAsync<CommentaryValidationException>(() => Process("bad\r\nname.txt", "hello"u8.ToArray()));
    }

    private async Task<ProcessedAttachment> Process(string name, byte[] bytes) => Assert.Single(await processor.ProcessAsync(
        [new AttachmentUpload(name, bytes.Length, () => new MemoryStream(bytes))], TestContext.Current.CancellationToken));

    private static byte[] TinyGif(int frameCount, ushort canvasWidth, ushort canvasHeight)
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        writer.Write("GIF89a"u8);
        writer.Write(canvasWidth);
        writer.Write(canvasHeight);
        writer.Write(new byte[] { 0x80, 0, 0, 0, 0, 0, 255, 255, 255 });
        for (int index = 0; index < frameCount; index++)
        {
            writer.Write(new byte[] { 0x2c, 0, 0, 0, 0, 1, 0, 1, 0, 0, 2, 2, 0x44, 0x01, 0 });
        }
        writer.Write((byte)0x3b);
        return stream.ToArray();
    }
}
