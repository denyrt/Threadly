using Microsoft.Extensions.Options;

namespace Threadly.Infrastructure.Captcha;

public sealed class TurnstileOptions
{
    public const string SectionName = "Turnstile";
    public string SiteKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
    public string ExpectedAction { get; set; } = "comment_create";
    public string[] AllowedHostnames { get; set; } = [];
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
    public bool TestMode { get; set; }
}

public sealed class TurnstileOptionsValidator(bool isDevelopment) : IValidateOptions<TurnstileOptions>
{
    private static readonly string[] TestSiteKeys =
    ["1x00000000000000000000AA", "2x00000000000000000000AB", "1x00000000000000000000BB", "2x00000000000000000000BB", "3x00000000000000000000FF"];
    private static readonly string[] TestSecretKeys =
    ["1x0000000000000000000000000000000AA", "2x0000000000000000000000000000000AA", "3x0000000000000000000000000000000AA"];

    public ValidateOptionsResult Validate(string? name, TurnstileOptions options)
    {
        List<string> failures = [];
        if (string.IsNullOrWhiteSpace(options.SiteKey)) failures.Add("Turnstile:SiteKey is required.");
        if (string.IsNullOrWhiteSpace(options.SecretKey)) failures.Add("Turnstile:SecretKey is required.");
        if (options.Timeout <= TimeSpan.Zero || options.Timeout > TimeSpan.FromSeconds(30))
            failures.Add("Turnstile:Timeout must be greater than zero and at most 30 seconds.");
        if (string.IsNullOrEmpty(options.ExpectedAction) || options.ExpectedAction.Length > 32
            || options.ExpectedAction.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-'))
            failures.Add("Turnstile:ExpectedAction must contain 1–32 ASCII letters, digits, underscores or hyphens.");

        bool testSiteKey = TestSiteKeys.Contains(options.SiteKey, StringComparer.Ordinal);
        bool testSecretKey = TestSecretKeys.Contains(options.SecretKey, StringComparer.Ordinal);
        if (options.TestMode)
        {
            if (!isDevelopment || !testSiteKey || !testSecretKey)
                failures.Add("Turnstile test mode requires Development and official test keys.");
        }
        else
        {
            if (testSiteKey || testSecretKey) failures.Add("Turnstile test keys require explicit Development test mode.");
            if (options.AllowedHostnames.Length == 0 || options.AllowedHostnames.Any(hostname =>
                    string.IsNullOrWhiteSpace(hostname) || Uri.CheckHostName(hostname) == UriHostNameType.Unknown || hostname.Contains('*')))
                failures.Add("Turnstile:AllowedHostnames must contain exact hostnames without schemes, ports or wildcards.");
        }
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
