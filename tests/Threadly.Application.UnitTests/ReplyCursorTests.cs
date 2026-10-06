using Threadly.Application.Commentaries;
using Xunit;

namespace Threadly.Application.UnitTests;

public sealed class ReplyCursorTests
{
    [Fact]
    public void RoundTrip_PreservesTicksIdAndParent()
    {
        ReplyCursor cursor = new(Guid.NewGuid(), new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc).AddTicks(1234567), Guid.NewGuid());
        Assert.Equal(cursor, ReplyCursor.Decode(cursor.Encode(), cursor.ParentId));
        Assert.Equal(DateTimeKind.Utc, ReplyCursor.Decode(cursor.Encode(), cursor.ParentId).CreatedAtUtc.Kind);
        Assert.Throws<CommentaryValidationException>(() => ReplyCursor.Decode(cursor.Encode(), Guid.NewGuid()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("  ")]
    [InlineData("invalid!")]
    public void Decode_RejectsMalformedInput(string value)
    {
        Assert.Equal("cursor", Assert.Throws<CommentaryValidationException>(() => ReplyCursor.Decode(value, Guid.NewGuid())).Field);
    }
}
