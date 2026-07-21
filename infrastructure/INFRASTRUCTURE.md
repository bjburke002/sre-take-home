# Candidate API Infrastructure as Code Documentation

## Overview

This project leverages Pulumi's Azure-Native provider to define and manage infrastructure definitions as C# code. 
Pulumi code can be found in:
```
/infrastucture/<service>
```

## Project Layout

Each individual Azure service has been defined as its own project with a Dev and Test stack to avoid hardcoding and allow a more templated approach.

Each stack contains its own individual configs set using
```
pulumi config 
```

Each project is structed as below:
```
./
├── Pulumi.yaml             # Pulumi project configuration
├── Pulumi.<env>.yaml       # Stack related Configs/Secrets/Environment variables 
├── Program.cs              # C# code defining Azure resources
├── <ProjectName>.csproj    # .NET project file
├── bin/                    # Build output (auto-generated)
└── obj/                    # Build artifacts (auto-generated)
```
# Infrastructure Considerations
For the purposes of this exercise, some assumptions and tradeoffs were made:
1. For ease of use and management, one shared Azure Container Registry was created and used between both AKS clusters.
2. To avoid having to build self-hosted GitHub runners in my Azure tenant and add additional complexity to this project, I left the ACR and AKS API server open to public internet. In production, this would not be feasible and we would have both private ACR and AKS clusters, reachable only from within peered VNETs. Best practice would be to have a Bastion host should anyone need to touch the AKS API, and Self-Hosted build/deploy agents to build and deploy our container images within our private VNET
3. To streamline the process of creating fully functional infrastructure, I decided to install AKS and any core services (cert-manager, Traefik ingress, kube-prometheus) Helm charts directly from Pulumi.  As a practical trade-off, we could easily manage these applications through a GitHub actions workflow but I felt it made more sense to spin everything up at once and use GitHub Actions only for "business related" applications.
4. Because I wanted this project to feel "live" and have it running on real infrastructure, I utilized a personal domain that I own and manage in AWS. Should you want to validate this IaC, some changes will need to be made to use a DNS host/domain under your ownership, which are detailed below. Otherwise, cert-manager will not be able to assign certificates to your Ingress objects.
5. I decided to manage state with the default Pulumi cloud (no shared backend) and secrets/configs/envs using the native pulumi config command as a trade off for ease of use. In a real-world situation, we would ideally leverage a secrets management tool.
6. Containers are using a chiseled image and running as non-root, to mock some security best practices.
7. Readiness and Liveness probes were configured to use `/health/ready` and `/health/live` respectively. They will fail on a non-HTTP/200 return.
8. Pod resource requests and limitations were both configured along with Horizontal Pod Autoscaling to mock some real-world practices. These are not very finely tuned just yet, but they exist and are active. 

## Standing up the Azure resources

Currently, the resources used for this project are live in an Azure subscription. 

As the AKS project relies on outputs from the networking and ACR projects, the workflow runs in this order:
```
networking -> acr -> aks
```
### A note before spinning up the AKS project
The only change that will need to be made here is to change the following secrets set using pulumi config. These map to my personal DNS host, and will need to be adjusted to one that you own to test end-to-end:
- awsAccessKeyId
- awsSecretAccessKey

You can change these with 
```
pulumi config <secret_name> <secret_value> --secret 
```

To validate configurations work as expected, you can follow the steps below:
1. Create a new project in /infrastructure/networking. Name the default stack "dev":
    ```
    pulumi new azure-csharp
    ```
2. Create a new stack in /infrastructure/networking:
    ```
    pulumi stack new test
    ```
3. Choose which stack you want to deploy:
    ```
    pulumi select stack <name>
    ```
4. Preview and deploy the stack:
    ```
    pulumi preview
    pulumi up
    ```
5. When resources are no longer needed:
    ```
    pulumi destroy
    ```
6. Follow the above steps in /infrastructure/acr.
7. After the ACR is spun up, follow the above steps in /infrastructure/aks