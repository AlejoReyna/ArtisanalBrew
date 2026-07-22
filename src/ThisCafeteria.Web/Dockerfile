# syntax=docker/dockerfile:1
# Build context must be the repository root:
#   docker build -f src/ThisCafeteria.Web/Dockerfile -t thiscafeteria-web .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/ThisCafeteria.Domain/ThisCafeteria.Domain.csproj src/ThisCafeteria.Domain/
COPY src/ThisCafeteria.Application/ThisCafeteria.Application.csproj src/ThisCafeteria.Application/
COPY src/ThisCafeteria.Infrastructure/ThisCafeteria.Infrastructure.csproj src/ThisCafeteria.Infrastructure/
COPY src/ThisCafeteria.Web/ThisCafeteria.Web.csproj src/ThisCafeteria.Web/
RUN dotnet restore src/ThisCafeteria.Web/ThisCafeteria.Web.csproj

COPY src/ThisCafeteria.Domain/ src/ThisCafeteria.Domain/
COPY src/ThisCafeteria.Application/ src/ThisCafeteria.Application/
COPY src/ThisCafeteria.Infrastructure/ src/ThisCafeteria.Infrastructure/
COPY src/ThisCafeteria.Web/ src/ThisCafeteria.Web/

# Publish must re-run restore now that sources are present: the Web SDK only
# adds the implicit Microsoft.AspNetCore.App.Internal.Assets package (source of
# _framework/blazor.web.js in .NET 10) when it can see the Razor sources, so a
# csproj-only restore leaves it out of project.assets.json and --no-restore
# publishes without any _framework assets. The earlier restore layer still
# pre-warms the NuGet cache, so this second restore is a fast incremental pass.
RUN dotnet publish src/ThisCafeteria.Web/ThisCafeteria.Web.csproj \
    --configuration Release \
    --output /app/publish

# Fail the image build loudly if the Blazor framework script is ever missing
# again - without it the browser gets a 404, no circuit connects, and every
# @onclick on the site is dead.
RUN test -f /app/publish/wwwroot/_framework/blazor.web.js

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# curl is not present in the base aspnet image; needed for the HEALTHCHECK below.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app/publish .

# The chain registry reads deployment manifests from ./deployments relative to
# the working directory (/app). They live at the repo root, outside any project,
# so publish never picks them up; copy them explicitly from the build context.
# Without this the container finds no manifests and falls back to the addressless
# default chain stub. Env vars (ARTISANALBREW_*_MANIFEST) can still override.
COPY deployments/ ./deployments/

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD ["curl", "-f", "http://127.0.0.1:8080/health/ready"]

USER $APP_UID
ENTRYPOINT ["dotnet", "ThisCafeteria.Web.dll"]
