# syntax=docker/dockerfile:1.7
# ---- build -------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/ ./src/
RUN dotnet publish src/Host/Platform.Api/Platform.Api.csproj -c Release -o /app /p:UseAppHost=false

# EF Core migration bundles: one self-contained executable per module (run before deploying a new version:
#   /migrations/migrate-identity --connection "$DB" && /migrations/migrate-tenancy --connection "$DB" && ...).
ARG TARGETARCH
RUN dotnet tool install --global dotnet-ef --version 10.0.12 && export PATH="$PATH:/root/.dotnet/tools" \
 && rid="linux-$([ "$TARGETARCH" = "arm64" ] && echo arm64 || echo x64)" \
 && for m in Identity Tenancy People Groups Events Giving Content Communications; do \
      dotnet ef migrations bundle --project src/Modules/$m/Platform.Modules.$m --startup-project src/Host/Platform.Api \
        --configuration Release --self-contained -r $rid -o /migrations/migrate-$(echo $m | tr A-Z a-z) --force || exit 1; \
    done

# ---- runtime -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=1
COPY --from=build /app ./
COPY --from=build /migrations /migrations
USER $APP_UID
EXPOSE 8080
# Orchestrators should probe GET /health/live (liveness) and /health/ready (readiness).
ENTRYPOINT ["dotnet", "Platform.Api.dll"]
