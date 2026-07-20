using Pulumi;
using Pulumi.AzureNative.Resources;
using System.Linq;
using System.Collections.Generic;
using AzureNative = Pulumi.AzureNative;

return await Pulumi.Deployment.RunAsync(() =>
{

    // Create an Azure Resource Group
    var resourceGroup = new AzureNative.Resources.ResourceGroup("resourceGroup", new()
    {
        Location = "eastus2",
        ResourceGroupName = "candidate-api-acr-eastus2-rg",
    });

    // Create Azure Container Registry
    var containerRegistry = new AzureNative.ContainerRegistry.Registry("registryResource", new()
    {
        ResourceGroupName = resourceGroup.Name,
        Sku = new AzureNative.ContainerRegistry.Inputs.SkuArgs
        {
            Name = "Basic",
        },
        NetworkRuleBypassOptions = "AzureServices",
        Location = "eastus2",
        AdminUserEnabled = false,
        PublicNetworkAccess = "Enabled",
        RegistryName = $"candidateapiACR",
        DataEndpointEnabled = false,
        AnonymousPullEnabled = false,
        Tags =
        {
            {"service", "candidate-api" },
            {"environment", "shared"}
        },
        ZoneRedundancy = "Disabled",

    });
    
    // Output the acrID so we can ingest it for IAM setup in the kubernetes project
    return new Dictionary<string, object>
    {
        ["acrID"] = containerRegistry.Id
    };

});
