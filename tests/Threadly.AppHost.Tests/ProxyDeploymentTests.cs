using Aspire.Hosting.Docker.Resources;
using Aspire.Hosting.Docker.Resources.ComposeNodes;
using Threadly.AppHost;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace Threadly.AppHost.Tests;

public sealed class ProxyDeploymentTests
{
    [Fact]
    public void GeneratedComposeKeepsServicesAndUsesAStaticProxyOnItsOwnNetwork()
    {
        ComposeFile compose = new();
        compose.Networks["aspire"] = new Network { Name = "aspire", Driver = "bridge" };
        compose.Services["threadly-api"] = new Service { Name = "threadly-api", Networks = ["aspire"] };
        compose.Services["threadly-web"] = new Service
        {
            Name = "threadly-web",
            Image = "web-image",
            Networks = ["aspire"],
            Ports = ["8080:80"],
            Environment = new() { ["API_UPSTREAM"] = "http://threadly-api:8080" }
        };
        string yaml = ProxyDeployment.Configure(compose.ToYaml(), "172.30.80.0/24", "172.30.80.10");
        YamlStream parsed = new();
        parsed.Load(new StringReader(yaml));
        var root = (YamlMappingNode)parsed.Documents[0].RootNode;
        var services = (YamlMappingNode)root.Children["services"];
        var web = (YamlMappingNode)services.Children["threadly-web"];
        Assert.Equal("web-image", ((YamlScalarNode)web.Children["image"]).Value);
        var networks = (YamlMappingNode)web.Children["networks"];
        Assert.Single(networks.Children);
        var proxy = (YamlMappingNode)networks.Children[ProxyDeployment.NetworkName];
        Assert.Equal("172.30.80.10", ((YamlScalarNode)proxy.Children["ipv4_address"]).Value);
        Assert.Contains("API_UPSTREAM", yaml);
        Assert.Contains("172.30.80.0/24", yaml);
        var api = (YamlMappingNode)services.Children["threadly-api"];
        Assert.Equal(["aspire", ProxyDeployment.NetworkName], ((YamlSequenceNode)api.Children["networks"]).Children.Select(node => ((YamlScalarNode)node).Value));
        Assert.Equal(yaml, ProxyDeployment.Configure(yaml, "172.30.80.0/24", "172.30.80.10"));
    }

    [Theory]
    [InlineData("0.0.0.0/0", "172.30.80.10")]
    [InlineData("172.30.80.0/24", "172.31.80.10")]
    public void InvalidNetworkSettingsFailEarly(string subnet, string address) =>
        Assert.Throws<InvalidOperationException>(() => ProxyDeployment.Configure("", subnet, address));
}
