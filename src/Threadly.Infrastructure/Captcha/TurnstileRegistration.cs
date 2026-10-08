using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Threadly.Application.Commentaries.Captcha;

namespace Threadly.Infrastructure.Captcha;

public static class TurnstileRegistration
{
    public static IServiceCollection AddTurnstile(this IServiceCollection services, IConfiguration configuration, bool isDevelopment)
    {
        services.AddSingleton<IValidateOptions<TurnstileOptions>>(new TurnstileOptionsValidator(isDevelopment));
        services.AddOptions<TurnstileOptions>().Bind(configuration.GetSection(TurnstileOptions.SectionName)).ValidateOnStart();
#pragma warning disable EXTEXP0001 // Explicitly remove the inherited pipeline; Siteverify tokens are single-use.
        services.AddHttpClient(TurnstileVerifier.ClientName, (provider, client) =>
        {
            client.Timeout = provider.GetRequiredService<IOptions<TurnstileOptions>>().Value.Timeout;
            client.MaxResponseContentBufferSize = 16 * 1024;
        })
            .RemoveAllResilienceHandlers()
            .RemoveAllLoggers()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
#pragma warning restore EXTEXP0001
        services.AddTransient<ICaptchaVerifier>(provider => new TurnstileVerifier(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(TurnstileVerifier.ClientName),
            provider.GetRequiredService<IOptions<TurnstileOptions>>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<TurnstileVerifier>>()));
        return services;
    }
}
