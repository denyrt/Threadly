using System.Globalization;
using System.Text;

namespace Threadly.Application.Commentaries;

public sealed record ReplyCursor(Guid ParentId, DateTime CreatedAtUtc, Guid Id)
{
    public string Encode()
    {
        string value = string.Create(CultureInfo.InvariantCulture, $"1:{ParentId:N}:{CreatedAtUtc.Ticks}:{Id:N}");
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static ReplyCursor Decode(string value, Guid parentId)
    {
        if (value.Length is > 0 and <= 160 && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
        {
            try
            {
                string base64 = value.Replace('-', '+').Replace('_', '/');
                base64 = base64.PadRight((base64.Length + 3) / 4 * 4, '=');
                string[] parts = Encoding.UTF8.GetString(Convert.FromBase64String(base64)).Split(':');
                if (parts.Length == 4 && parts[0] == "1"
                    && Guid.TryParseExact(parts[1], "N", out Guid cursorParent) && cursorParent == parentId
                    && long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out long ticks)
                    && ticks >= DateTime.MinValue.Ticks && ticks <= DateTime.MaxValue.Ticks
                    && Guid.TryParseExact(parts[3], "N", out Guid id) && id != Guid.Empty)
                {
                    ReplyCursor cursor = new(cursorParent, new DateTime(ticks, DateTimeKind.Utc), id);
                    if (cursor.Encode() == value)
                    {
                        return cursor;
                    }
                }
            }
            catch (FormatException)
            {
                // Invalid Base64 is reported through the same validation contract.
            }
        }

        throw new CommentaryValidationException("cursor", "The cursor is invalid for this comment.");
    }
}
