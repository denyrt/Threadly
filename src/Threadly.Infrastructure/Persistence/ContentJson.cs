using System.Text.Json;
using Threadly.Domain.Commentaries;

namespace Threadly.Infrastructure.Persistence;

internal static class ContentJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(IReadOnlyList<TextContentBlock> content) => JsonSerializer.Serialize(content, Options);
    public static IReadOnlyList<TextContentBlock> Deserialize(string json) =>
        Array.AsReadOnly(JsonSerializer.Deserialize<TextContentBlock[]>(json, Options)!);
}
