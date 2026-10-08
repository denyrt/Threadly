using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Net;
using Threadly.Application.Commentaries.Captcha;
using Threadly.Infrastructure.Captcha;
using Xunit;

namespace Threadly.Infrastructure.UnitTests;

public sealed class TurnstileVerifierTests
{
    [Theory]
    [InlineData("{\"success\":true,\"hostname\":\"threadly.example\",\"action\":\"comment_create\"}", CaptchaVerification.Valid)]
    [InlineData("{\"success\":true,\"hostname\":\"other.example\",\"action\":\"comment_create\"}", CaptchaVerification.Invalid)]
    [InlineData("{\"success\":true,\"hostname\":\"threadly.example\",\"action\":\"login\"}", CaptchaVerification.Invalid)]
    [InlineData("{\"success\":false,\"error-codes\":[\"invalid-input-response\"]}", CaptchaVerification.Invalid)]
    [InlineData("{\"success\":false,\"error-codes\":[\"timeout-or-duplicate\"]}", CaptchaVerification.Invalid)]
    [InlineData("{\"success\":false,\"error-codes\":[\"invalid-input-secret\"]}", CaptchaVerification.Unavailable)]
    [InlineData("{\"success\":false,\"error-codes\":[\"bad-request\"]}", CaptchaVerification.Unavailable)]
    [InlineData("{\"success\":false,\"error-codes\":[\"internal-error\"]}", CaptchaVerification.Unavailable)]
    [InlineData("{\"success\":false,\"error-codes\":[\"new-unknown-error\"]}", CaptchaVerification.Unavailable)]
    [InlineData("{\"success\":true}", CaptchaVerification.Unavailable)]
    [InlineData("{\"success\":false}", CaptchaVerification.Unavailable)]
    [InlineData("{}", CaptchaVerification.Unavailable)]
    [InlineData("null", CaptchaVerification.Unavailable)]
    [InlineData("<html>Unavailable</html>", CaptchaVerification.Unavailable)]
    public async Task ValidatesProviderResponse(string json, CaptchaVerification expected)
    {
        using Transport transport = new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(json) }));
        using ServiceProvider services = BuildServices(transport);
        Assert.Equal(expected, await services.GetRequiredService<ICaptchaVerifier>().VerifyAsync("private-token", TestContext.Current.CancellationToken));
        Assert.Equal(1, transport.Calls);
        Assert.Equal("secret=server-secret&response=private-token", transport.Body);
    }

    [Theory]
    [InlineData("http")]
    [InlineData("network")]
    [InlineData("timeout")]
    public async Task OverridesActualServiceDefaultsAndNeverRetries(string failure)
    {
        using Transport transport = new(async (_, token) =>
        {
            if (failure == "network") throw new HttpRequestException("Transport failed.");
            if (failure == "timeout") await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        });
        using ServiceProvider services = BuildServices(transport, TimeSpan.FromMilliseconds(100));
        Assert.Equal(CaptchaVerification.Unavailable,
            await services.GetRequiredService<ICaptchaVerifier>().VerifyAsync("token", TestContext.Current.CancellationToken));
        Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task CallerCancellationPropagatesWithoutRetry()
    {
        using CancellationTokenSource cancellation = new();
        using Transport transport = new(async (_, token) =>
        {
            await cancellation.CancelAsync();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using ServiceProvider services = BuildServices(transport);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => services.GetRequiredService<ICaptchaVerifier>().VerifyAsync("token", cancellation.Token));
        Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task OfficialTestMetadataIsAcceptedOnlyWithValidatedDevelopmentTestMode()
    {
        using Transport transport = new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent("{\"success\":true,\"hostname\":\"dummy.invalid\",\"action\":\"test\"}") }));
        using ServiceProvider services = BuildServices(transport, testMode: true);
        Assert.Equal(CaptchaVerification.Valid,
            await services.GetRequiredService<ICaptchaVerifier>().VerifyAsync("XXXX.DUMMY.TOKEN.XXXX", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("SiteKey")]
    [InlineData("SecretKey")]
    [InlineData("ExpectedAction")]
    [InlineData("AllowedHostnames")]
    [InlineData("Timeout")]
    [InlineData("TestMode")]
    [InlineData("TestSiteKey")]
    [InlineData("TestSecretKey")]
    public void ProductionRequiresConfigurationAndRejectsTestKeys(string invalid)
    {
        TurnstileOptions options = new()
        {
            SiteKey = "site-key",
            SecretKey = "secret-key",
            AllowedHostnames = ["threadly.example"]
        };
        switch (invalid)
        {
            case "SiteKey": options.SiteKey = ""; break;
            case "SecretKey": options.SecretKey = ""; break;
            case "ExpectedAction": options.ExpectedAction = ""; break;
            case "AllowedHostnames": options.AllowedHostnames = []; break;
            case "Timeout": options.Timeout = TimeSpan.Zero; break;
            case "TestMode": options.TestMode = true; break;
            case "TestSiteKey": options.SiteKey = "1x00000000000000000000AA"; break;
            case "TestSecretKey": options.SecretKey = "1x0000000000000000000000000000000AA"; break;
        }
        Assert.True(new TurnstileOptionsValidator(false).Validate(null, options).Failed);
    }

    private static ServiceProvider BuildServices(Transport transport, TimeSpan? timeout = null, bool testMode = false)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.AddServiceDefaults();
        builder.Services.AddTurnstile(new ConfigurationBuilder().Build(), testMode);
        builder.Services.Configure<TurnstileOptions>(options =>
        {
            options.SiteKey = testMode ? "1x00000000000000000000AA" : "site-key";
            options.SecretKey = testMode ? "1x0000000000000000000000000000000AA" : "server-secret";
            options.AllowedHostnames = ["threadly.example"];
            options.TestMode = testMode;
            options.Timeout = timeout ?? TimeSpan.FromSeconds(10);
        });
        builder.Services.AddHttpClient(TurnstileVerifier.ClientName).ConfigurePrimaryHttpMessageHandler(() => transport);
        return builder.Services.BuildServiceProvider();
    }

    private sealed class Transport(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://challenges.cloudflare.com/turnstile/v0/siteverify", request.RequestUri?.AbsoluteUri);
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return await send(request, cancellationToken);
        }
    }
}
