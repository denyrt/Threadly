using System.Buffers;
using System.ComponentModel.DataAnnotations;

namespace Threadly.Domain;

internal static class Validation
{
    private static readonly SearchValues<char> AlphanumericValues;
    private static readonly EmailAddressAttribute EmailAddressAttr;

    static Validation()
    {
        AlphanumericValues = SearchValues.Create("0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ");
        EmailAddressAttr = new EmailAddressAttribute();
    }

    public static string RequireString(string value, int maxLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        var trimmedValue = value.Trim();

        if (trimmedValue.Length > maxLength)
        {
            throw new ArgumentException($"Value cannot exceed {maxLength} characters.", parameterName);
        }

        return trimmedValue;
    }

    public static string RequireUsername(string value, int maxLength, string parameterName)
    {
        var trimmedValue = RequireString(value, maxLength, parameterName);

        if (trimmedValue.AsSpan().IndexOfAnyExcept(AlphanumericValues) != -1)
        {
            throw new ArgumentException("Value can contain only alphanumeric characters.", parameterName);
        }

        return trimmedValue;
    }

    public static string RequireEmail(string value, int maxLength, string parameterName)
    {
        var trimmedValue = RequireString(value, maxLength, parameterName);
        var lowerCaseValue = trimmedValue.ToLowerInvariant();

        if (!EmailAddressAttr.IsValid(lowerCaseValue))
        {
            throw new ArgumentException("Value must be an email address.", parameterName);
        }

        return trimmedValue;
    }

    public static string RequireCommentary(string value, int maxLength, string parameterName)
    {
        return RequireString(value, maxLength, parameterName);
    }

    public static DateTime RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Value must be UTC.", parameterName);
        }

        return value;
    }
}
