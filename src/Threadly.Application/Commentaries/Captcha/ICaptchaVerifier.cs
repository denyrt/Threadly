namespace Threadly.Application.Commentaries.Captcha;

public interface ICaptchaVerifier
{
    Task<CaptchaVerification> VerifyAsync(string token, CancellationToken cancellationToken);
}

public enum CaptchaVerification
{
    Valid,
    Invalid,
    Unavailable
}

public sealed class CaptchaUnavailableException() : Exception("Verification is temporarily unavailable. Please try again shortly.");
