using System.Net;
using YamlDotNet.RepresentationModel;

namespace Threadly.AppHost;

public static class ProxyDeployment
{
    public const string NetworkName = "comment-proxy";

    public static string Configure(string yaml, string subnet, string proxyAddress)
    {
        if (!IPNetwork.TryParse(subnet, out IPNetwork network) || network.PrefixLength is < 16 or > 30
            || !IPAddress.TryParse(proxyAddress, out IPAddress? address)
            || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork || !network.Contains(address))
            throw new InvalidOperationException("Deployment proxy settings require an IPv4 subnet (/16–/30) containing the Nginx address.");

        // Aspire 13.6 models only the short (list) form of service networks. This AppHost
        // publish step supplies the long form; there is no manually maintained Compose fork.
        YamlStream document = new();
        document.Load(new StringReader(yaml));
        var root = (YamlMappingNode)document.Documents[0].RootNode;
        var networks = (YamlMappingNode)root.Children["networks"];
        networks.Children[NetworkName] = new YamlMappingNode
        {
            { "driver", "bridge" },
            { "ipam", new YamlMappingNode { { "config", new YamlSequenceNode(new YamlMappingNode { { "subnet", subnet } }) } } }
        };
        var services = (YamlMappingNode)root.Children["services"];
        var api = (YamlMappingNode)services.Children["threadly-api"];
        var apiNetworks = (YamlSequenceNode)api.Children["networks"];
        if (!apiNetworks.Children.Contains(new YamlScalarNode(NetworkName))) apiNetworks.Add(NetworkName);
        var web = (YamlMappingNode)services.Children["threadly-web"];
        web.Children["networks"] = new YamlMappingNode
        {
            { NetworkName, new YamlMappingNode { { "ipv4_address", proxyAddress } } }
        };
        using StringWriter output = new();
        document.Save(output, assignAnchors: false);
        return output.ToString();
    }
}
