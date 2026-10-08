using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Threadly.Application.Commentaries.Captcha;

namespace Threadly.Infrastructure.Captcha;

public sealed class TurnstileVerifier(HttpClient client, IOptions<TurnstileOptions> options, ILogger<TurnstileVerifier> logger)
    : ICaptchaVerifier
{
    public const string ClientName = "turnstile";

    public async Task<CaptchaVerification> VerifyAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 2048) return CaptchaVerification.Invalid;
        try
        {
            TurnstileOptions settings = options.Value;
            using FormUrlEncodedContent body = new(new Dictionary<string, string>
            {
                ["secret"] = settings.SecretKey,
                ["response"] = token
            });
            using HttpResponseMessage response = await client.PostAsync(
                "https://challenges.cloudflare.com/turnstile/v0/siteverify", body, cancellationToken);
            if (!response.IsSuccessStatusCode) return Refuse("http", CaptchaVerification.Unavailable);

            SiteverifyResponse? result = await response.Content.ReadFromJsonAsync<SiteverifyResponse>(cancellationToken);
            if (result?.Success is null) return Refuse("protocol", CaptchaVerification.Unavailable);
            if (!result.Success.Value)
            {
                // Unknown, secret and integration errors are operational failures, never user mistakes.
                string[] tokenErrors = ["missing-input-response", "invalid-input-response", "timeout-or-duplicate"];
                return result.ErrorCodes is { Length: > 0 } && result.ErrorCodes.All(code => tokenErrors.Contains(code, StringComparer.Ordinal))
                    ? Refuse("token", CaptchaVerification.Invalid)
                    : Refuse("provider", CaptchaVerification.Unavailable);
            }
            if (result.ErrorCodes is { Length: > 0 }) return Refuse("protocol", CaptchaVerification.Unavailable);

            // Official dummy keys return synthetic hostname/action, independent of the widget configuration.
            // TestMode is accepted only in Development with official test keys by startup validation.
            if (!settings.TestMode && (string.IsNullOrWhiteSpace(result.Hostname) || string.IsNullOrWhiteSpace(result.Action)))
                return Refuse("protocol", CaptchaVerification.Unavailable);
            if (!settings.TestMode && (!settings.AllowedHostnames.Contains(result.Hostname, StringComparer.OrdinalIgnoreCase)
                || !string.Equals(settings.ExpectedAction, result.Action, StringComparison.Ordinal)))
                return Refuse("policy", CaptchaVerification.Invalid);

            return CaptchaVerification.Valid;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Refuse("timeout", CaptchaVerification.Unavailable);
        }
        catch (HttpRequestException)
        {
            return Refuse("network", CaptchaVerification.Unavailable);
        }
        catch (JsonException)
        {
            return Refuse("protocol", CaptchaVerification.Unavailable);
        }
        catch (OptionsValidationException)
        {
            return Refuse("configuration", CaptchaVerification.Unavailable);
        }
    }

    private CaptchaVerification Refuse(string category, CaptchaVerification outcome)
    {
        logger.LogWarning("CAPTCHA refused: {Category}; trace {TraceId}", category, Activity.Current?.TraceId.ToString());
        return outcome;
    }

    private sealed class SiteverifyResponse
    {
        public bool? Success { get; init; }
        public string? Hostname { get; init; }
        public string? Action { get; init; }
        [JsonPropertyName("error-codes")]
        public string[]? ErrorCodes { get; init; }
    }
}
