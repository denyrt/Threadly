using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Ganss.Xss;
using Threadly.Application.Commentaries;
using Threadly.Application.Commentaries.Content;
using Threadly.Domain.Commentaries;

namespace Threadly.Infrastructure.Content;

public sealed class ContentProcessor : IContentProcessor
{
    private static readonly HashSet<string> Tags = ["i", "strong", "code", "a"];
    private readonly HtmlSanitizer sanitizer;

    public ContentProcessor()
    {
        sanitizer = new HtmlSanitizer();
        sanitizer.AllowedTags.Clear();
        sanitizer.AllowedTags.UnionWith(Tags);
        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedAttributes.Add("href");
        sanitizer.UriAttributes.Clear();
        sanitizer.UriAttributes.Add("href");
        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.UnionWith(["http", "https"]);
        sanitizer.AllowedCssProperties.Clear();
        sanitizer.AllowedAtRules.Clear();
        sanitizer.AllowDataAttributes = false;
    }

    public ProcessedContent Process(IReadOnlyList<ContentBlockInput>? content)
    {
        if (content is null || content.Count == 0) Fail("Content must contain at least one text block.");
        // A nonempty block consumes at least one UTF-16 unit; bound work before parsing.
        if (content!.Count > ContentLimits.InputBudget) Fail("Message budget cannot exceed 2000 units.");
        int budget = 0;
        foreach (ContentBlockInput? block in content)
        {
            if (block is null || block.Type != "text") Fail("Only text content blocks are supported.");
            if (string.IsNullOrEmpty(block!.Html)) Fail("Each text block requires html.");
            if (block.Html!.Length > ContentLimits.InputBudget - budget) Fail("Message budget cannot exceed 2000 units.");
            budget += block.Html.Length;
        }

        List<TextContentBlock> result = [];
        bool hasVisibleText = false;
        int normalizedLength = 0;
        foreach (ContentBlockInput block in content)
        {
            string html = block.Html!;
            ValidateSource(html);
            HtmlParser parser = new(new HtmlParserOptions { IsStrictMode = true });
            using var document = new HtmlParser().ParseDocument("");
            INodeList nodes;
            try
            {
                nodes = parser.ParseFragment(html, document.Body!);
            }
            catch (HtmlParseException)
            {
                Fail("HTML is malformed. Check tags, attributes and entities.");
                throw;
            }
            foreach (INode node in nodes) ValidateNode(node);
            string normalized = sanitizer.Sanitize(html);
            normalizedLength += normalized.Length;
            if (normalizedLength > ContentLimits.NormalizedHtmlLength) Fail("Normalized content exceeds its technical size limit.");
            hasVisibleText |= nodes.Any(node => !string.IsNullOrWhiteSpace(node.TextContent));
            result.Add(new TextContentBlock(normalized));
        }
        if (!hasVisibleText) Fail("Content must include visible, non-whitespace text.");
        return new ProcessedContent(result.AsReadOnly(), budget, ContentLimits.InputBudget);
    }

    private static void ValidateNode(INode node)
    {
        if (node is IElement element)
        {
            if (!Tags.Contains(element.LocalName)) Fail("Only i, strong, code and a tags are allowed.");
            foreach (IAttr attribute in element.Attributes)
            {
                if (element.LocalName != "a" || attribute.Name != "href") Fail("Only href on a links is allowed.");
                // The parser has decoded HTML entities in the attribute value.
                string value = attribute.Value;
                if (value.Any(char.IsWhiteSpace) || value.Any(char.IsControl) || value.Contains('\\')
                    || !(value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    || !Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || string.IsNullOrEmpty(uri.Host)
                    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    Fail("Link href must be an absolute http or https URL.");
            }
            if (element.LocalName == "a" && !element.HasAttribute("href")) Fail("Links require an absolute http or https href.");
        }
        else if (node.NodeType != NodeType.Text) Fail("Comments, declarations and other HTML nodes are not allowed.");
        foreach (INode child in node.ChildNodes) ValidateNode(child);
    }

    // Validate the original tag stream before the HTML parser can repair nesting or EOF.
    // Attribute syntax/entities are additionally validated by the strict HTML parser.
    private static void ValidateSource(string html)
    {
        Stack<string> open = new();
        for (int index = 0; index < html.Length; index++)
        {
            if (html[index] == '\0') Fail("HTML cannot contain null characters.");
            if (html[index] != '<') continue;
            int cursor = index + 1;
            if (cursor == html.Length) Fail("Incomplete HTML. Escape a literal < as &lt;.");
            bool closing = html[cursor] == '/';
            if (closing) cursor++;
            if (cursor == html.Length) Fail("Incomplete closing tag.");
            if (!char.IsAsciiLetter(html[cursor]))
            {
                if (closing || html[cursor] is '!' or '?') Fail("Invalid or forbidden HTML markup.");
                continue; // Let the strict parser report malformed literal less-than signs.
            }
            int start = cursor;
            while (cursor < html.Length && char.IsAsciiLetter(html[cursor])) cursor++;
            string tag = html[start..cursor].ToLowerInvariant();
            if (!Tags.Contains(tag)) Fail("Only i, strong, code and a tags are allowed.");
            if (cursor < html.Length && html[cursor] != '>' && !IsHtmlSpace(html[cursor])) Fail("Malformed tag.");
            char quote = '\0';
            for (; cursor < html.Length; cursor++)
            {
                char current = html[cursor];
                if (quote != '\0')
                {
                    if (current == quote) quote = '\0';
                }
                else if (current is '\'' or '"') quote = current;
                else if (current == '>') break;
                else if (current == '<') Fail("Malformed tag.");
                if (closing && current != '>' && !IsHtmlSpace(current)) Fail("Closing tags cannot have attributes.");
            }
            if (cursor == html.Length) Fail("Unclosed HTML tag or attribute.");
            if (closing)
            {
                if (!open.TryPop(out string? expected) || expected != tag) Fail("HTML tags must be correctly nested and closed.");
            }
            else
            {
                if (tag == "a" && open.Contains("a")) Fail("Links cannot be nested.");
                open.Push(tag);
            }
            index = cursor;
        }
        if (open.Count != 0) Fail("All HTML tags must be closed.");
    }

    private static bool IsHtmlSpace(char value) => value is ' ' or '\t' or '\r' or '\n' or '\f';
    private static void Fail(string message) => throw new CommentaryValidationException("content", message);
}
