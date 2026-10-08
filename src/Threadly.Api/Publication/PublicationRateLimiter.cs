using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;

namespace Threadly.Api.Publication;

public sealed class PublicationRateLimiter(IOptions<PublicationOptions> options) : IDisposable
{
    private readonly PartitionedRateLimiter<string> limiter = PartitionedRateLimiter.Create<string, string>(ip =>
        RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = options.Value.PermitLimit,
            Window = options.Value.Window,
            QueueLimit = 0,
            AutoReplenishment = true
        }));

    public RateLimitLease Attempt(IPAddress? address) => limiter.AttemptAcquire(
        (address?.IsIPv4MappedToIPv6 == true ? address.MapToIPv4() : address)?.ToString() ?? "unknown");

    public void Dispose() => limiter.Dispose();
}

public sealed class PublicationRateLimitMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, PublicationRateLimiter limiter)
    {
        bool publication = context.GetEndpoint()?.Metadata.GetMetadata<PublicationAttribute>() is not null
            || (HttpMethods.IsPost(context.Request.Method)
                && string.Equals(context.Request.Path.Value?.TrimEnd('/'), "/api/comments", StringComparison.OrdinalIgnoreCase));
        if (!publication)
        {
            await next(context);
            return;
        }
        // Exactly one acquisition per HTTP attempt, before the upload concurrency limiter.
        // Combining a fixed-window GlobalLimiter and endpoint limiter in ASP.NET middleware
        // can acquire the global lease twice when the endpoint limiter rejects a request.
        using RateLimitLease lease = limiter.Attempt(context.Connection.RemoteIpAddress);
        if (lease.IsAcquired)
        {
            await next(context);
            return;
        }
        if (lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
            context.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        await Results.Problem(new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Too many publication attempts. Please wait before trying again.",
            Extensions = { ["code"] = "publication_rate_limit" }
        }).ExecuteAsync(context);
    }
}
