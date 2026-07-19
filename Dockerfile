FROM mcr.microsoft.com/dotnet/sdk:10.0.200 AS build
ARG BUILD_CONFIGURATION=Release
ARG VERSION=1.0.0
WORKDIR /src

COPY global.json .

COPY src/CandidateApi/CandidateApi.csproj src/CandidateApi/
COPY src/CandidateApi.Contracts/CandidateApi.Contracts.csproj src/CandidateApi.Contracts/

RUN dotnet restore src/CandidateApi/CandidateApi.csproj

COPY . .

RUN dotnet build src/CandidateApi/CandidateApi.csproj \
    -c "${BUILD_CONFIGURATION}" \
    -p:Version="${VERSION}" \
    -p:ContinuousIntegrationBuild=true \
    -o /app/build 

FROM build AS publish
RUN dotnet publish src/CandidateApi/CandidateApi.csproj \
    -c "${BUILD_CONFIGURATION}" \
    -p:Version="${VERSION}" \
    --no-restore \
    -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS final

WORKDIR /app

EXPOSE 8080

ENV ASPNETCORE_HTTP_PORTS=8080 \
	DOTNET_EnableDiagnostics=0

COPY --from=publish /app/publish .

USER app

ENTRYPOINT ["dotnet", "CandidateApi.dll"]