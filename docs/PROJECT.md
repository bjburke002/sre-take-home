# Project Overview

This contains documentation for the overall project and work delivered as part of this exercise. At a high level, the main additions were:
- Two pipelines.
    - A build pipeline `build-and-test.yaml`that triggers on a pull request being opened or pushed to, that builds a dotnet solution, packs and publishes a preview version of the Contracts NuGets package for this project, validates a Dockerfile can be built, and lints all necessary Helm charts. No push or deployment is attempted here.
    - A deploy pipeline `deploy.yaml` that triggers on push to main (a PR merge) and performs a build similar to the PR/validation workflow. 
- Infrastructure as Code leverating Pulumi written in C# to match the codebase and define a shared Azure Container registry, core networking (a VNet, Subnet and Network Security Group), and Azure Kubernetes clusters for 2 stacks (Dev and Test).
- Minor custom instrumentation added to expose custom metrics, defined in `/src/CandidateApi/Services/CandidateApiMetrics.cs` which are exposed and scraped by Prometheus.
- Grafana dashboard analyzing service health and SLI compliance.

## Deliverables

### Pipeline Details

All GitHub actions workflows can be found in `/.github/workflows`. Additional pipeline details can be found in `/docs/PIPELINES.md`

`build-and-test.yaml` is a simple build/validation pipeline. It runs on any opened Pull Request or push to a PR branch, restores and builds the dotnet solution for this project. 
- After a successful build, `dotnet test` is run against the solution for unit testing. Results are logged and then published as artifacts on the pipeline run, for analysis.
- Once build and unit testing is complete, the Contracts NuGet is packed and published to the GitHub packages repository, a Docker image is built for validation, and all Helm charts are linted then rendered so verify formatting and successful values injection. 

`deploy.yaml` also builds and tests the solution, and builds a Docker image. After successful build, it authenticates to an Azure tenant using an OIDC federated credential to push the image to Azure Container Registry and then deploys this container image to Dev AKS. If this deployment is successful, the same container image is promoted to a separate Test cluster.

### Infrastructure as Code Details

All IaC files can be found in `/infrastructure`. Additional details on execution of these Pulumi projects can be found in `/infrastructure/INFRASTRUCTURE.md`

Three Pulumi projects written in C# (to mirror the code base) were created to handle automated spin-up and destruction of the cloud resources needed to platform this exercise. `/infrastructure/acr` is a shared resource, but `/infrastructure/networking` and `/infrastructure/aks` both have two different stacks with unique configs to define both Dev and Test instances of the same templates/modules.
- `/infrastructure/networking` defines core networking for the Azure platform. It creates a new VNet, a subnet within this VNet and an NSG connected to the subnet to be used by the AKS node pools.
- `/infrastructure/acr` creates an Azure Container Registry to store whatever we build as part of our deploy pipeline.
- `/infrastructure/aks` defines a managed AKS cluster, its node pools, and installs several core services (cert-manager, Traefik, kube-prometheus, etc.) with Helm charts defined in Pulumi. With this approach, we only have to spin up the clusters and not maintain additional Helm charts or application installs.

### Grafana Dashboard/SLI/SLO Details

Custom instrumentations can be found in `/src/CandidateApi/Services/CandidateApiMetrics.cs`. A JSON definition of the Grafana dashboard can be found in `/dashboard/*.json`

SLO definition, and details about SLI, error budget, burn rate alerting and custom instrumentation can all be found in `/dashboard/SLODOCS.md`

### SLO Assumptions
- Custom instrumentation was build around Availability metrics for the API
- SLO of 99.9% Availability in a rolling 30 day period
- As a secondary metric, request latency is also measured.
    - Latency SLI of 95% <500ms


# Validation
- Open a PR based on main. `build-and-test` will run and if all changes are valid, `Build and Test`, `Pack and Publish Contracts NuGet`, `Build Docker Image`, and `Validate and Lint Helm Chart` will pass and a new preview version of the Contracts NuGet will be published to the Package feed.
- Merge the PR into main at which point `deploy.yaml` will run, build a new Docker image and deploy that to Dev and Test AKS. After both pass, curl requests to `https://<env>-api.burketechnologies.net/health/live` or `https://<env>-api.burketechnologies.net/health/ready` will return service health. You will also be able to curl `/api/work-items` or simply the root `<env>-api.burketechnologies.net` and receive an HTTP/200 return

# Tradeoffs
- To avoid having to configure self-hosted GitHub runners in my Azure tenant, both the ARC and Kubernetes API servers are open to public internet. This is solely a trade-off for the purpose of this being an exercise--in a production setup this would not be feasible and they would all be private and limited to peered VNET connections. This was a conscious decision I made simply to reduce complexity a bit at the cost of this being a true production ready product.
- I chose Helm for package deployments for the sake of scaling in the future. Managing Helm templates and values adds some additional complexity up front that we could most likely have avoided with a solution like Kustomize, but Helm's my preferred tool because as your number of environments grows it scales more efficiently. I also chose Helm for its easy rollbacks and state/history management. Should we ever need a rollback after a buggy deployment, Helm makes it much simpler. For me, this was adding some initial complexity for ease of use down the line.
- I managed the Pulumi state with Pulumi cloud instead of a backend, and deployed via CLI instead of working up a pipeline which is definitely something we would do in a real-world situation. In my view the best practice is to strictly limit any IaC deploys to a pipeline, but adding this would have increased the scope of this project so I went with local management instead and config/secrets management using `pulumi config` across different stacks.
- I chose overall Availability as my SLI for the sake of simplicity. My custom instrumentation is almost entirely surrounding HTTP requests, etc. so that I could get a fuller picture of things like latency in such a simple app. Because the API here doesn't really do a lot, I felt this was the most tangible metric we could work with that wasn't K8s related like pod resources.