using System;
using System.Collections.Generic;
using System.Text;
using Pulumi;

using Pulumi.AzureNative.Authorization;
using Pulumi.AzureNative.ContainerService;
using Pulumi.AzureNative.Resources;

using Pulumi.Kubernetes;
using Pulumi.Kubernetes.Helm.V3;
using Pulumi.Kubernetes.Types.Inputs.Helm.V3;
using Pulumi.Kubernetes.Types.Inputs.Meta.V1;

using AzureNative = Pulumi.AzureNative;


return await Pulumi.Deployment.RunAsync(() =>
{
    // Ingest configs
    var config = new Pulumi.Config();

    var env = config.Require("env");
    var location = config.Require("location");
    var agentpool = config.Require("agentpool");
    var userpool = config.Require("userpool");
    var DnsPrefix = config.Require("DnsPrefix");

    var acrStack = new StackReference($"bjburke002/acr/{env}");

    var acrID = acrStack.GetOutput("acrID").Apply(v => v?.ToString() ?? "");

    var subnetStack = new StackReference($"bjburke002/networking/{env}");
    var subnetID = subnetStack.GetOutput("aksSubnetID").Apply(v => v?.ToString() ?? "");

    // Create an Azure Resource Group
    var resourceGroup = new AzureNative.Resources.ResourceGroup("resourceGroup", new()
    {
        Location = location,
        ResourceGroupName = $"{env}-aks-eastus2-rg",
    });


    // Create a user assigned managed identity for the cluster to use
    var userAssignedIdentity = new AzureNative.ManagedIdentity.UserAssignedIdentity("userAssignedIdentity", new()
    {
        Location = location,
        ResourceGroupName = $"{env}-aks-eastus2-rg",
        ResourceName = $"{env}-aks-managed-identity"
    },
    new CustomResourceOptions
    {
        DependsOn = { resourceGroup }
    });

    // Create AKS control plane and managed instances
    var managedCluster = new AzureNative.ContainerService.ManagedCluster("managedCluster", new()
    {
        Identity = new AzureNative.ContainerService.Inputs.ManagedClusterIdentityArgs
        {
            Type = AzureNative.ContainerService.ResourceIdentityType.SystemAssigned
        },
        AgentPoolProfiles = new[]
        {
            new AzureNative.ContainerService.Inputs.ManagedClusterAgentPoolProfileArgs
            {
                MinCount = 1,
                MaxCount = 3,
                EnableAutoScaling = true,
                EnableNodePublicIP = false,
                Mode = "System",
                Name = agentpool,
                OsType = AzureNative.ContainerService.OSType.Linux,
                Type = AzureNative.ContainerService.AgentPoolType.VirtualMachineScaleSets,
                VmSize = "Standard_D2s_V3",
                VnetSubnetID = subnetID,
            },
            new AzureNative.ContainerService.Inputs.ManagedClusterAgentPoolProfileArgs
            {
                MinCount = 1,
                MaxCount = 3,
                EnableAutoScaling = true,
                EnableNodePublicIP = false,
                Mode = "User",
                Name = userpool,
                OsType = AzureNative.ContainerService.OSType.Linux,
                Type = AzureNative.ContainerService.AgentPoolType.VirtualMachineScaleSets,
                VmSize = "Standard_D2_V3",
                VnetSubnetID = subnetID,
            }
        },
        //ApiServerAccessProfile = new AzureNative.ContainerService.Inputs.ManagedClusterAPIServerAccessProfileArgs
        //{
        //    EnablePrivateCluster = true,
        //    EnablePrivateClusterPublicFQDN = true,
        //},
        AutoScalerProfile = new AzureNative.ContainerService.Inputs.ManagedClusterPropertiesAutoScalerProfileArgs
        {
            ScaleDownDelayAfterAdd = "15m",
            ScanInterval = "20s",
        },
        DnsPrefix = DnsPrefix,
        EnableRBAC = true,
        IdentityProfile =
        {
            {"managedIdentity", new AzureNative.ContainerService.Inputs.UserAssignedIdentityArgs
            {
            ClientId = userAssignedIdentity.ClientId,
            ObjectId = userAssignedIdentity.PrincipalId,
            ResourceId = userAssignedIdentity.Id
            } },
        },
        KubernetesVersion = "1.36",
        Location = location,
        NetworkProfile = new AzureNative.ContainerService.Inputs.ContainerServiceNetworkProfileArgs
        {
            LoadBalancerProfile = new AzureNative.ContainerService.Inputs.ManagedClusterLoadBalancerProfileArgs
            {
                ManagedOutboundIPs = new AzureNative.ContainerService.Inputs.ManagedClusterLoadBalancerProfileManagedOutboundIPsArgs
                {
                    Count = 2,
                },
            },
            LoadBalancerSku = AzureNative.ContainerService.LoadBalancerSku.Standard,
            OutboundType = AzureNative.ContainerService.OutboundType.LoadBalancer,
        },
        OidcIssuerProfile = new AzureNative.ContainerService.Inputs.ManagedClusterOIDCIssuerProfileArgs
        {
            Enabled = true
        },
        SecurityProfile = new AzureNative.ContainerService.Inputs.ManagedClusterSecurityProfileArgs
        {
            // Pod identity has been deprecated so using Workload Identity instead
            WorkloadIdentity = new AzureNative.ContainerService.Inputs.ManagedClusterSecurityProfileWorkloadIdentityArgs
            {
                Enabled = true,
            },
        },
        ResourceGroupName = resourceGroup.Name,
        ResourceName = $"{env}-aks-eastus2-001",
        Sku = new AzureNative.ContainerService.Inputs.ManagedClusterSKUArgs
        {
            Name = "Base",
            Tier = AzureNative.ContainerService.ManagedClusterSKUTier.Free,
        },

    });

    // Grab Kubelet managed identity principal ID to assign acrPull it
    var kubeletPrincipalId = managedCluster.IdentityProfile.Apply(profile => profile?["kubeletidentity"].ObjectId);

    // Give ACR the acrPull role
    var acrPullRoleAssignment = new RoleAssignment("acrPullRole", new RoleAssignmentArgs
    {
        Scope = acrID,
        PrincipalId = kubeletPrincipalId,
        PrincipalType = PrincipalType.ServicePrincipal,
        RoleDefinitionId = "/providers/Microsoft.Authorization/roleDefinitions/7f951dda-4ed3-4680-a7ca-43fe172d538d"
    });

    // Create credentials and k8s provider
    var credentials = Output.Tuple(resourceGroup.Name, managedCluster.Name)
        .Apply(names =>
            ListManagedClusterUserCredentials.Invoke(
                new ListManagedClusterUserCredentialsInvokeArgs
                {
                    ResourceGroupName = names.Item1,
                    ResourceName = names.Item2
                }));

    // We use a Kubernetes provider here to make API calls. Using ARM wouldn't let us create a namespace
    var k8sProvider = new Pulumi.Kubernetes.Provider(
        "aks-provider",
        new Pulumi.Kubernetes.ProviderArgs
        {
            KubeConfig = credentials.Apply(c =>
                Encoding.UTF8.GetString(
                    Convert.FromBase64String(c.Kubeconfigs[0].Value)))
        });

    // Create namespaces in AKS cluster
    var Namespace = new Pulumi.Kubernetes.Core.V1.Namespace(
        "namespace",
        new Pulumi.Kubernetes.Types.Inputs.Core.V1.NamespaceArgs
        {
            Metadata = new Pulumi.Kubernetes.Types.Inputs.Meta.V1.ObjectMetaArgs
            {
                Name = env
            }
        },
        new CustomResourceOptions
        {
            Provider = k8sProvider,
            DependsOn = { managedCluster }
        });
    /*
        In this section, we are going to install necessary Helm charts to get the cluster up and running.
        Cert-Manager for TLS, and Traefik for Ingress.
    */
    
    var certManager = new Release("cert-manager", new ReleaseArgs
    {
        Chart = "cert-manager",
        RepositoryOpts = new RepositoryOptsArgs
        {
            Repo = "https://charts.jetstack.io"
        },
        Version = "v1.18.2",
        Namespace = "cert-manager",
        CreateNamespace = true,
        Values =
        {
            ["crds"] = new  Dictionary<string, object>
            {
                ["enabled"] = true,
            }
        }
    },
    new CustomResourceOptions
    {
        Provider = k8sProvider,
    });

    var traefikIngress = new Release("traefik", new ReleaseArgs
    {
        Name = "traefik",
        Chart = "traefik",
        Namespace = "traefik",
        CreateNamespace = true,

        RepositoryOpts = new RepositoryOptsArgs
        {
            Repo = "https://traefik.github.io/charts"
        },

        Values =
        {
            ["service"] = new Dictionary<string, object>
            {
                ["type"] = "LoadBalancer",
            },

            ["ingressClass"] = new Dictionary<string, object>
            {
                ["enabled"] = true,
                ["isDefaultClass"] = true,
            }
        }
    },
    new CustomResourceOptions
    {
        Provider = k8sProvider,
    });
});
