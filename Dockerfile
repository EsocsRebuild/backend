# syntax=docker/dockerfile:1.7
# ==============================================================================
# Production Dockerfile for .NET 10 Modular Monolith
# Multi-stage, security-hardened, non-root runtime with EF Core migration bundles.
# ==============================================================================

# ---- Stage 1: Base Runtime ---------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS base
WORKDIR /app

LABEL org.opencontainers.image.title="Platform API" \
      org.opencontainers.image.description="Modular Monolith Backend for Church Administration Platform" \
      org.opencontainers.image.vendor="ESOCS Digital Technologies" \
      org.opencontainers.image.licenses="Proprietary"

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=1 \
    DOTNET_GCDump_Enable=0 \
    DOTNET_EnableDiagnostics=0 \
    DOTNET_RUNNING_IN_CONTAINER=true

# Security: Non-root user (app UID 1654 in .NET 8+ noble/alpine images)
USER $APP_UID
EXPOSE 8080

# ---- Stage 2: Restore & Dependencies Cache -----------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS restore
WORKDIR /src

# Copy central package management and configuration
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig Platform.slnx ./

# Copy all project definition files to cache the restore layer
COPY src/BuildingBlocks/Platform.SharedKernel/Platform.SharedKernel.csproj ./src/BuildingBlocks/Platform.SharedKernel/
COPY src/BuildingBlocks/Platform.Application/Platform.Application.csproj ./src/BuildingBlocks/Platform.Application/
COPY src/BuildingBlocks/Platform.Infrastructure/Platform.Infrastructure.csproj ./src/BuildingBlocks/Platform.Infrastructure/
COPY src/BuildingBlocks/Platform.Web/Platform.Web.csproj ./src/BuildingBlocks/Platform.Web/

COPY src/Host/Platform.Api/Platform.Api.csproj ./src/Host/Platform.Api/

COPY src/Modules/Identity/Platform.Modules.Identity.Contracts/Platform.Modules.Identity.Contracts.csproj ./src/Modules/Identity/Platform.Modules.Identity.Contracts/
COPY src/Modules/Identity/Platform.Modules.Identity/Platform.Modules.Identity.csproj ./src/Modules/Identity/Platform.Modules.Identity/

COPY src/Modules/Tenancy/Platform.Modules.Tenancy.Contracts/Platform.Modules.Tenancy.Contracts.csproj ./src/Modules/Tenancy/Platform.Modules.Tenancy.Contracts/
COPY src/Modules/Tenancy/Platform.Modules.Tenancy/Platform.Modules.Tenancy.csproj ./src/Modules/Tenancy/Platform.Modules.Tenancy/

COPY src/Modules/People/Platform.Modules.People.Contracts/Platform.Modules.People.Contracts.csproj ./src/Modules/People/Platform.Modules.People.Contracts/
COPY src/Modules/People/Platform.Modules.People/Platform.Modules.People.csproj ./src/Modules/People/Platform.Modules.People/

COPY src/Modules/Groups/Platform.Modules.Groups.Contracts/Platform.Modules.Groups.Contracts.csproj ./src/Modules/Groups/Platform.Modules.Groups.Contracts/
COPY src/Modules/Groups/Platform.Modules.Groups/Platform.Modules.Groups.csproj ./src/Modules/Groups/Platform.Modules.Groups/

COPY src/Modules/Events/Platform.Modules.Events.Contracts/Platform.Modules.Events.Contracts.csproj ./src/Modules/Events/Platform.Modules.Events.Contracts/
COPY src/Modules/Events/Platform.Modules.Events/Platform.Modules.Events.csproj ./src/Modules/Events/Platform.Modules.Events/

COPY src/Modules/Giving/Platform.Modules.Giving.Contracts/Platform.Modules.Giving.Contracts.csproj ./src/Modules/Giving/Platform.Modules.Giving.Contracts/
COPY src/Modules/Giving/Platform.Modules.Giving/Platform.Modules.Giving.csproj ./src/Modules/Giving/Platform.Modules.Giving/

COPY src/Modules/Content/Platform.Modules.Content/Platform.Modules.Content.csproj ./src/Modules/Content/Platform.Modules.Content/

COPY src/Modules/Communications/Platform.Modules.Communications.Contracts/Platform.Modules.Communications.Contracts.csproj ./src/Modules/Communications/Platform.Modules.Communications.Contracts/
COPY src/Modules/Communications/Platform.Modules.Communications/Platform.Modules.Communications.csproj ./src/Modules/Communications/Platform.Modules.Communications/

# Cache restore layer
RUN dotnet restore src/Host/Platform.Api/Platform.Api.csproj

# ---- Stage 3: Build & Publish ------------------------------------------------
FROM restore AS build
WORKDIR /src

# Copy all source files
COPY src/ ./src/

# Compile and publish optimized production release
RUN dotnet publish src/Host/Platform.Api/Platform.Api.csproj \
    -c Release \
    --no-restore \
    -o /app/publish \
    /p:UseAppHost=false

# ---- Stage 4: EF Core Migration Bundles ---------------------------------------
FROM restore AS migrations
WORKDIR /src
COPY src/ ./src/

ARG TARGETARCH
RUN dotnet tool install --global dotnet-ef --version 10.0.12 && export PATH="$PATH:/root/.dotnet/tools" \
 && rid="linux-$([ "$TARGETARCH" = "arm64" ] && echo arm64 || echo x64)" \
 && mkdir -p /migrations \
 && for m in Identity Tenancy People Groups Events Giving Content Communications; do \
      dotnet ef migrations bundle \
        --project src/Modules/$m/Platform.Modules.$m \
        --startup-project src/Host/Platform.Api \
        --configuration Release \
        --self-contained \
        -r $rid \
        -o /migrations/migrate-$(echo $m | tr A-Z a-z) \
        --force || exit 1; \
    done

# ---- Stage 5: Final Production Container -------------------------------------
FROM base AS final
WORKDIR /app

# Copy published application binaries
COPY --from=build --chown=$APP_UID:$APP_UID /app/publish ./

# Copy migration bundles for pre-deploy execution
COPY --from=migrations --chown=$APP_UID:$APP_UID /migrations /migrations

USER $APP_UID

# Orchestrators and docker healthchecks probe HTTP health endpoints:
# GET /health/live (Liveness) and GET /health/ready (Readiness)
HEALTHCHECK --interval=15s --timeout=3s --start-period=10s --retries=3 \
  CMD wget --no-verbose --tries=1 --spider http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "Platform.Api.dll"]
