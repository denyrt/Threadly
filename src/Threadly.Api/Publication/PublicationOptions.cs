namespace Threadly.Api.Publication;

[AttributeUsage(AttributeTargets.Method)]
public sealed class PublicationAttribute : Attribute;

public sealed class PublicationOptions
{
    public int PermitLimit { get; set; } = 10;
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);
}

public sealed class TrustedProxyOptions
{
    public string[] KnownProxies { get; set; } = [];
    public string[] KnownNetworks { get; set; } = [];
    public int ForwardLimit { get; set; } = 1;
}
