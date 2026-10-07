using System.Text.Json.Serialization;

namespace Threadly.Application.Commentaries.Content;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ContentBlockInput(string? Type, string? Html);
