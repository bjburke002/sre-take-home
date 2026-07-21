# GitHub Actions Workflow Overview

## Pipelines

`build-and-test.yaml` runs when a pull request is opened or pushed to. It builds the dotnet project, runs unit tests, packs and publishes a Preview version of the Contracts NuGet, verifies a Dockerfile can be built, and lints all Helm templates.

`deploy.yaml` runs on any push to main, which in this case would be when a Pull Request is merged. It also builds the project, runs unit tests, packs and publishes a new version of the Contracts NuGet package, and expands on it with the following flow:
1. After the build and test portion is complete, ``deploy.yaml`` builds a Docker image tagged with the commit SHA and pushes it to Azure Container Registry.
2. After a successful build, a new version of the Contracts artifact is packed and published in parallel.
3. The workflow then deploys to a Dev instance of Azure Kubernetes using a custom Helm chart.
4. The Helm deployment is run as ``helm upgrade --install --atomic --wait`` which gives us automatic rollback in the event that the deployment times out or otherwise fails. If it returns successfully, then the pipeline moves on to a quick smoke test.
5. The smoke test queries ``/health/live`` to see if the status returned is "Alive" over several attempts. If after the threshold, the endpoint is still not returning "Alive", the smoke test fails and alerts.
6. If the Dev deployment is successful, Helm deploys to a Test instance of Azure Kubernetes using the same Container artifact and deployment methods.

# OIDC/Federated Credentials

Clarity on the OIDC workflow can be found below:
- The GitHub Runner requests an OIDC token from the GitHub OIDC provider and is assigned a signed JWT.
- The runner exchanges that OIDC token with Microsoft Entra ID using an Azure App Registration configured with a federated credential. No secrets are exchanged.
- EntraID validates the token claims and returns an Azure access token.
- The runner uses this access token to authenticate with Azure resources.

## Helm Templates

The workloads deployed by these GitHub workflows are structured and packaged as a Helm chart leveraging Helm templates. All `candidate-api` specific Helm resources can be found in `/charts`.

The templates defined give us a variablized manifest that can be deployed to Azure Kubernetes with values files used to inject environment specific args into the Kubernetes manifests. 

In `/charts` we define a basic Kubernetes Deployment, Ingress, Service, and Horizontal Pod Autoscaler through templates. There is also a `values.yaml` file that is used for **default values**.

Environment specific values files can be found in `/charts/environments`. Any changes to the resource-specific Kubernetes specifications (i.e. adjusting a CPU or Memory request, enabling or disabling autoscaling) that need to be made should be done in the `values.<env>.yaml` file that corresponds with the environment you'd like to adjust. 

To manually package and deploy the Helm chart (should you need to do this), you can do the following:
1. In `/charts`, run `helm package .` which will generate a tarball.
2. Run `helm upgrade --install --atomic candidate-api .\<chart-name>.tgz -f .\environments\values.<env>.yaml -n <env>`
3. Helm will attempt to install the Helm chart. If it fails or times out, it will automatically roll back to the previous version.

## Azure Credential considerations

As one of the steps in `deploy.yaml`, the GitHub runner connects to Azure using:
- ${{ secrets.AZURE_CLIENT_ID }}
- ${{ secrets.AZURE_TENANT_ID }}
- ${{ secrets.AZURE_SUBSCRIPTION_ID }}

The clientID corresponds to an Azure App Registration that has been configured to use an OIDC issued federated credential for `repo:bjburke002/sre-take-home:ref:refs/heads/main`.  

This can be adjusted for your own purposes as needed, but the Repository secrets will also have to be updated alongside for any validation.