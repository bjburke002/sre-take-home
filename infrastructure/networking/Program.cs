using Pulumi;
using Pulumi.AzureNative.Resources;
using Pulumi.AzureNative.Network;
using Pulumi.AzureNative.Network.Inputs;
using System.Collections.Generic;
using AzureNative = Pulumi.AzureNative;

return await Pulumi.Deployment.RunAsync(() =>
{

    // Ingest configs
    var config = new Pulumi.Config();

    var env = config.Require("env");

    // Create an Azure Resource Group
    var resourceGroup = new ResourceGroup("resourceGroup", new()
    {
        Location = "eastus2",
        ResourceGroupName = $"{env}-vnets-eastus2-rg" 
    });

    var NSG = new AzureNative.Network.NetworkSecurityGroup("nsg", new()
    {
        ResourceGroupName = resourceGroup.Name,
        Location = "eastus2",
        NetworkSecurityGroupName = $"aks-subnet-{env}-nsg",

    });

    var vNet = new AzureNative.Network.VirtualNetwork("vNet", new()
    {
        AddressSpace = new AzureNative.Network.Inputs.AddressSpaceArgs
        {
            AddressPrefixes = new[]
            {
                "10.10.0.0/16",
            },
        },
        Location = "eastus2",
        ResourceGroupName = resourceGroup.Name,
        VirtualNetworkName = $"coterie-api-vnet-{env}"
    });

    var aksSubnet = new AzureNative.Network.Subnet("aksSubnet", new()
    {
        AddressPrefix = "10.10.0.0/22",
        ResourceGroupName = resourceGroup.Name,
        VirtualNetworkName = vNet.Name,
        NetworkSecurityGroup = new AzureNative.Network.Inputs.NetworkSecurityGroupArgs
        {
            Id = NSG.Id,
        },

        SubnetName = $"aks-subnet-{env}"
    });

    //Output subnet ID so it can be attached to other resources
    return new Dictionary<string, object>
    {
        ["aksSubnetID"] = aksSubnet.Id
    };
});
