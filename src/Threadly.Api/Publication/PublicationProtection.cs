using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Threadly.Api.Publication;

public static class PublicationProtection
{
    public static IServiceCollection AddPublicationProtection(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PublicationOptions>().Bind(configuration.GetSection("PublicationRateLimit"))
            .Validate(options => options.PermitLimit > 0 && options.Window > TimeSpan.Zero && options.Window <= TimeSpan.FromDays(1),
                "PublicationRateLimit requires a positive PermitLimit and a Window of at most one day.")
            .ValidateOnStart();
        services.AddOptions<TrustedProxyOptions>().Bind(configuration.GetSection("TrustedProxies"))
            .Validate(options => options.ForwardLimit > 0 && options.KnownProxies.All(value => IPAddress.TryParse(value, out _))
                && options.KnownNetworks.All(value => System.Net.IPNetwork.TryParse(value, out var network) && network.PrefixLength > 0),
                "TrustedProxies requires explicit IP addresses/networks and a positive ForwardLimit. Unrestricted networks are forbidden.")
            .ValidateOnStart();
        services.AddOptions<ForwardedHeadersOptions>().Configure<IOptions<TrustedProxyOptions>>((options, configured) =>
        {
            TrustedProxyOptions proxies = configured.Value;
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = proxies.ForwardLimit;
            options.RequireHeaderSymmetry = true;
            // With explicit configuration trust exactly those proxies. Otherwise retain the
            // framework's restricted loopback defaults for local development; never trust all peers.
            if (proxies.KnownProxies.Length + proxies.KnownNetworks.Length > 0)
            {
                options.KnownProxies.Clear();
                options.KnownIPNetworks.Clear();
            }
            foreach (string address in proxies.KnownProxies) options.KnownProxies.Add(IPAddress.Parse(address));
            foreach (string network in proxies.KnownNetworks) options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
        });
        services.AddSingleton<PublicationRateLimiter>();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                await Results.Problem(new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Uploads are busy. Please try again shortly.",
                    Extensions = { ["code"] = "upload_concurrency" }
                }).ExecuteAsync(context.HttpContext);
            };
        });
        return services;
    }
}
