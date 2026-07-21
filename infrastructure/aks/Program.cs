using System;
using System.Collections.Generic;
using System.Text;
using Pulumi;

using Pulumi.AzureNative.Authorization;
using Pulumi.AzureNative.ContainerService;
using Pulumi.AzureNative.Resources;


using KubeCustomResource = Pulumi.Kubernetes.ApiExtensions.CustomResource;
using KubeCustomResourceArgs = Pulumi.Kubernetes.ApiExtensions.CustomResourceArgs;
using Pulumi.Kubernetes.Yaml;
using Pulumi.Kubernetes.Core.V1;
using Pulumi.Kubernetes.Helm.V3;
using Pulumi.Kubernetes.Types.Inputs.Core.V1;
using Pulumi.Kubernetes.Types.Inputs.Helm.V3;
using Pulumi.Kubernetes.Types.Inputs.Meta.V1;

using AzureNative = Pulumi.AzureNative;
using System.Xml;
using System.Data.Common;


return await Pulumi.Deployment.RunAsync(() =>
{
    // Ingest configs
    var config = new Pulumi.Config();

    var env = config.Require("env");
    var location = config.Require("location");
    var agentpool = config.Require("agentpool");
    var userpool = config.Require("userpool");
    var DnsPrefix = config.Require("DnsPrefix");

    var acrStack = new StackReference($"bjburke002/acr/dev");

    var acrID = acrStack.GetOutput("acrID").Apply(v => v?.ToString() ?? "");

    var subnetStack = new StackReference($"bjburke002/networking/{env}");
    var subnetID = subnetStack.GetOutput("aksSubnetID").Apply(v => v?.ToString() ?? "");

    var awsAccessKeyId = config.RequireSecret("awsAccessKeyId");
    var awsSecretAccessKey = config.RequireSecret("awsSecretAccessKey");
    var awsRegion = config.Require("awsRegion");

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
            NetworkPlugin = AzureNative.ContainerService.NetworkPlugin.Azure,
            NetworkPluginMode = AzureNative.ContainerService.NetworkPluginMode.Overlay,
            NetworkPolicy = AzureNative.ContainerService.NetworkPolicy.Azure,

            // Pod network
            PodCidr = "192.168.0.0/16",

            //Service virtual IP range
            ServiceCidr = "10.20.0.0/16",
            DnsServiceIP = "10.20.0.10",
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

    // Give kubelet the acrPull role
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
        },
        
        new CustomResourceOptions
        {
            DependsOn = {managedCluster}
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
        DependsOn = {managedCluster}
    });

    // Secret to hold AWS credentials for external-dns to use to update Route53 records
    var route53Creds = new Secret("route53Creds", new SecretArgs
    {
        Metadata = new ObjectMetaArgs
        {
            Name = "route53-creds",
            Namespace = "cert-manager"
        },

        StringData =
        {
            {
                "secret-access-key",
                awsSecretAccessKey
            }
        }
    },
    new CustomResourceOptions
    {
        Provider = k8sProvider,
        DependsOn = { certManager },
    });

    // Create ClusterIssuer for cert-manager/letsencrypt

    var clusterIssuerYaml = Output.Format($@"
        apiVersion: cert-manager.io/v1
        kind: ClusterIssuer
        metadata:
            name: letsencrypt-{env}
        spec:
            acme:
                email: bjburke002@gmail.com
                server: https://acme-v02.api.letsencrypt.org/directory
                privateKeySecretRef:
                    name: letsencrypt-{env}
                solvers:
                - dns01:
                    route53:
                        region: {awsRegion}
                        accessKeyID: {awsAccessKeyId}
                        secretAccessKeySecretRef:
                            name: route53-creds
                            key: secret-access-key
        ");
    var clusterIssuer = new Pulumi.Kubernetes.Yaml.ConfigGroup($"letsencrypt-{env}", new Pulumi.Kubernetes.Yaml.ConfigGroupArgs
    {
        Yaml = clusterIssuerYaml
    },
    new ComponentResourceOptions
    {
        Provider = k8sProvider,
        DependsOn = { managedCluster, certManager, route53Creds }
    });

    // Ingress controller so we can make calls after setup
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
            },

            ["ports"] = new Dictionary<string, object>
            {
                ["web"] = new Dictionary<string, object>
                {
                    ["expose"] = new Dictionary<string,object>
                    {
                        ["default"] = true,
                    }
                },

                ["websecure"] = new Dictionary<string, object>
                {
                    ["expose"] = new Dictionary<string, object>
                    {
                        ["default"] = true,
                    },
                }
            }
        }
    },
    new CustomResourceOptions
    {
        Provider = k8sProvider,
        DependsOn = {managedCluster}
    });

    // Installing kube-prometheus-stack 
    var monitoringStack = new Release("kube-prometheus-stack", new ReleaseArgs
    {
        Name = $"monitoring-{env}",
        Namespace = "monitoring",
        CreateNamespace = true,

        Chart = "kube-prometheus-stack",

        RepositoryOpts = new RepositoryOptsArgs
        {
            Repo = "https://prometheus-community.github.io/helm-charts"
        },

        Values =
        {
            ["grafana"] = new Dictionary<string, object>
            {
                ["enabled"] = true,

                ["service"] = new Dictionary<string, object>
                {
                    ["type"] = "ClusterIP"
                },

                ["ingress"] = new Dictionary<string,object>
                {
                    ["enabled"] = true,
                    ["ingressClassName"] = "traefik",
                    ["hosts"] = new[]
                    {
                        $"grafana.{env}-api.burketechnologies.net"
                    },
                    ["annotations"] = new Dictionary<string, object>
                    {
                        ["cert-manager.io/cluster-issuer"] = $"letsencrypt-{env}"
                    },

                    ["tls"] = new []
                    {
                        new Dictionary<string, object>
                        {
                            ["secretName"] = "grafana-tls",
                            ["hosts"] = new[]
                            {
                                $"grafana.{env}-api.burketechnologies.net"
                            }
                        }
                    }
                },

            ["prometheus"] = new Dictionary<string, object>
            {
                ["enabled"] = true
            }
        }
    }
    },
    new CustomResourceOptions
    {
        Provider = k8sProvider,
        DependsOn = {managedCluster}
    });

    var serviceMonitorYaml = Output.Format($@"
    apiVersion: monitoring.coreos.com/v1
    kind: ServiceMonitor
    metadata:
        name: candidate-api
        namespace: monitoring
        labels:
            release: monitoring-{env}
    spec:
        namespaceSelector:
            matchNames:
                - {env}
        selector:
            matchLabels:
                app.kubernetes.io/component: api
        endpoints:
        - port: http
          path: /metrics
          interval: 15s  
    ");

    var candidateApiServiceMonitor = new Pulumi.Kubernetes.Yaml.ConfigGroup(
        $"candidate-api-servicemonitor-{env}",
        new Pulumi.Kubernetes.Yaml.ConfigGroupArgs
        {
            Yaml = serviceMonitorYaml
        },
        new ComponentResourceOptions
        {
            Provider = k8sProvider,
            DependsOn = {monitoringStack}
        }
    );
});
