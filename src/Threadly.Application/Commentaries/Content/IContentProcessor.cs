using Threadly.Domain.Commentaries;

namespace Threadly.Application.Commentaries.Content;

public interface IContentProcessor
{
    ProcessedContent Process(IReadOnlyList<ContentBlockInput>? content);
}

public sealed record ProcessedContent(IReadOnlyList<TextContentBlock> Content, int BudgetUsed, int BudgetLimit);

public static class ContentLimits
{
    public const int InputBudget = 2000;
    // Entity escaping and attribute quoting can expand otherwise valid input.
    public const int NormalizedHtmlLength = Commentary.MaxNormalizedContentLength;
    // Includes worst-case JSON escaping and block envelopes, but not attachment bytes.
    public const int JsonFieldLength = 262144;
}
