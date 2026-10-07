namespace Threadly.Domain.Commentaries;

public sealed record TextContentBlock(string Html)
{
    public string Type => "text";
}
