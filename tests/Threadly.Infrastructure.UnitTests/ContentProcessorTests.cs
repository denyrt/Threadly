using Threadly.Application.Commentaries;
using Threadly.Application.Commentaries.Content;
using Threadly.Infrastructure.Content;
using Xunit;

namespace Threadly.Infrastructure.UnitTests;

public sealed class ContentProcessorTests
{
    private readonly ContentProcessor processor = new();

    [Theory]
    [InlineData("plain text")]
    [InlineData("Привіт 🧵\n世界")]
    [InlineData("<i>italic <strong>bold <code>code</code></strong></i>")]
    [InlineData("<a href='https://example.com/?a=1&amp;b=2'><strong>link</strong></a>")]
    [InlineData("<a href=http://example.com>link</a>")]
    [InlineData("<a href='https://приклад.укр/шлях'>link</a>")]
    [InlineData("<code>&lt;tag&gt; &amp; x</code>")]
    [InlineData("a &amp; b &lt; c &gt; d &quot; &#39; &#x1F9F5;")]
    [InlineData("<STRONG>Uppercase</STRONG>")]
    [InlineData("1 &lt; 2 & 3 > 2")]
    public void AcceptsSafeContentAndNormalizationIsIdempotent(string html)
    {
        ProcessedContent result = Process(html);
        Assert.Equal(html.Length, result.BudgetUsed);
        Assert.Equal(2000, result.BudgetLimit);
        string normalized = Assert.Single(result.Content).Html;
        Assert.Equal(normalized, Assert.Single(Process(normalized).Content).Html);
    }

    [Theory]
    [InlineData("<i>unclosed")]
    [InlineData("<i><strong>bad</i></strong>")]
    [InlineData("<code>bad</i>")]
    [InlineData("<i/>text")]
    [InlineData("<i>text</i extra>")]
    [InlineData("<i>text</i/>")]
    [InlineData("<i")]
    [InlineData("text<")]
    [InlineData("<!-- comment -->text")]
    [InlineData("<!doctype html>text")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("<br>text")]
    [InlineData("<b>text</b>")]
    [InlineData("<strong style='color:red'>text</strong>")]
    [InlineData("<i class='x'>text</i>")]
    [InlineData("<code id='x'>text</code>")]
    [InlineData("<i href='https://example.com'>text</i>")]
    [InlineData("<a href='https://example.com' onclick='alert(1)'>text</a>")]
    [InlineData("<a href='https://example.com' href='http://example.com'>text</a>")]
    [InlineData("<a href='https://example.com'><a href='https://example.com'>nested</a></a>")]
    [InlineData("<a>missing</a>")]
    [InlineData("<a href='javascript:alert(1)'>text</a>")]
    [InlineData("<a href='jav&#x61;script:alert(1)'>text</a>")]
    [InlineData("<a href='java&#10;script:alert(1)'>text</a>")]
    [InlineData("<a href='data:text/html,bad'>text</a>")]
    [InlineData("<a href='file:///tmp/file'>text</a>")]
    [InlineData("<a href='//example.com'>text</a>")]
    [InlineData("<a href='&#47;&#47;example.com'>text</a>")]
    [InlineData("<a href='/path'>text</a>")]
    [InlineData("<a href='mailto:a@example.com'>text</a>")]
    [InlineData("<a href='https://'>text</a>")]
    [InlineData("<a href='https:\\example.com'>text</a>")]
    [InlineData("<a href=' https://example.com'>text</a>")]
    [InlineData("\0text")]
    [InlineData(" ")]
    [InlineData("<i></i>")]
    [InlineData("<strong>\n\t&nbsp;&#32;</strong>")]
    public void RejectsInvalidOrEmptyHtml(string html) =>
        Assert.Equal("content", Assert.Throws<CommentaryValidationException>(() => Process(html)).Field);

    [Fact]
    public void BudgetUsesRawUtf16AcrossBlocksAndIncludesMarkup()
    {
        string html = "<i>" + new string('x', 1991) + "🧵</i>";
        Assert.Equal(2000, Process(html).BudgetUsed);
        Assert.Throws<CommentaryValidationException>(() => Process(html + "x"));
        Assert.Equal(2000, processor.Process([new("text", new string('x', 1000)), new("text", new string('y', 1000))]).BudgetUsed);
        Assert.Throws<CommentaryValidationException>(() => processor.Process([new("text", new string('x', 1000)), new("text", new string('y', 1001))]));
    }

    [Fact]
    public void NormalizedOutputCanExceedInputBudgetWithoutDoubleEncoding()
    {
        ProcessedContent result = Process(new string('&', 2000));
        Assert.Equal(2000, result.BudgetUsed);
        Assert.Equal(string.Concat(Enumerable.Repeat("&amp;", 2000)), result.Content[0].Html);
        Assert.Equal("<code>&lt;x&gt;&amp;</code>", Process("<code>&lt;x&gt;&amp;</code>").Content[0].Html);
    }

    [Fact]
    public void RejectsMissingAndUnsupportedBlocks()
    {
        foreach (IReadOnlyList<ContentBlockInput>? content in new IReadOnlyList<ContentBlockInput>?[]
        {
            null, [], [null!], [new("quote", "text")], [new("other", "text")], [new(null, "text")],
            [new("text", null)], [new("text", "")], [new("text", "valid"), new("quote", "text")]
        }) Assert.Throws<CommentaryValidationException>(() => processor.Process(content));
    }

    private ProcessedContent Process(string html) => processor.Process([new("text", html)]);
}
