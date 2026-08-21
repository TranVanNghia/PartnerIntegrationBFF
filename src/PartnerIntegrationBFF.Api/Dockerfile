# Build stage — needs the full SDK to restore/compile.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj files first so `dotnet restore` is cached as its own Docker layer and only re-runs
# when a .csproj actually changes, not on every source file edit.
COPY src/PartnerIntegrationBFF.Api/PartnerIntegrationBFF.Api.csproj src/PartnerIntegrationBFF.Api/
RUN dotnet restore src/PartnerIntegrationBFF.Api/PartnerIntegrationBFF.Api.csproj

COPY src/PartnerIntegrationBFF.Api/ src/PartnerIntegrationBFF.Api/
RUN dotnet publish src/PartnerIntegrationBFF.Api/PartnerIntegrationBFF.Api.csproj -c Release -o /app --no-restore

# Runtime stage — the much smaller ASP.NET runtime image, no SDK/compiler included.
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "PartnerIntegrationBFF.Api.dll"]
