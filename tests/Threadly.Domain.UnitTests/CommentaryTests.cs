using Threadly.Domain.Commentaries;
using Xunit;

namespace Threadly.Domain.UnitTests;

public sealed class CommentaryTests
{
    [Fact]
    public void Constructor_AllowsRootsAndRepliesWithoutAStoredDepth()
    {
        Commentary comment = new("Reader", "reader@example.com", [new TextContentBlock("Root")], DateTime.UtcNow);
        Assert.Null(comment.ParentId);
        for (int depth = 0; depth < 100; depth++)
        {
            Commentary reply = new("Reader", "reader@example.com", [new TextContentBlock("Reply")], DateTime.UtcNow, comment.Id);
            Assert.Equal(comment.Id, reply.ParentId);
            Assert.NotEqual(comment.Id, reply.Id);
            comment = reply;
        }
        Assert.False(typeof(Commentary).GetProperty(nameof(Commentary.ParentId))!.SetMethod!.IsPublic);
    }

    [Fact]
    public void Constructor_RejectsEmptyParent()
    {
        Assert.Throws<ArgumentException>(() => new Commentary("Reader", "reader@example.com", [new TextContentBlock("Reply")], DateTime.UtcNow, Guid.Empty));
    }

    [Theory]
    [InlineData("bad_name", "reader@example.com", "Reply")]
    [InlineData("Reader", "invalid", "Reply")]
    [InlineData("Reader", "reader@example.com", "  ")]
    public void Constructor_UsesTheSameValidationForReplies(string username, string email, string text)
    {
        Assert.ThrowsAny<ArgumentException>(() => new Commentary(username, email, [new TextContentBlock(text)], DateTime.UtcNow, Guid.NewGuid()));
    }
}
