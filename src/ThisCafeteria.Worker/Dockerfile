# syntax=docker/dockerfile:1
# Build context must be the repository root:
#   docker build -f src/ThisCafeteria.Worker/Dockerfile -t thiscafeteria-worker .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/ThisCafeteria.Domain/ThisCafeteria.Domain.csproj src/ThisCafeteria.Domain/
COPY src/ThisCafeteria.Application/ThisCafeteria.Application.csproj src/ThisCafeteria.Application/
COPY src/ThisCafeteria.Infrastructure/ThisCafeteria.Infrastructure.csproj src/ThisCafeteria.Infrastructure/
COPY src/ThisCafeteria.Worker/ThisCafeteria.Worker.csproj src/ThisCafeteria.Worker/
RUN dotnet restore src/ThisCafeteria.Worker/ThisCafeteria.Worker.csproj

COPY src/ThisCafeteria.Domain/ src/ThisCafeteria.Domain/
COPY src/ThisCafeteria.Application/ src/ThisCafeteria.Application/
COPY src/ThisCafeteria.Infrastructure/ src/ThisCafeteria.Infrastructure/
COPY src/ThisCafeteria.Worker/ src/ThisCafeteria.Worker/

RUN dotnet publish src/ThisCafeteria.Worker/ThisCafeteria.Worker.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

# Infrastructure depends on Microsoft.AspNetCore.Identity.EntityFrameworkCore, so the
# Worker needs the aspnet shared runtime even though it isn't a web app itself.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Npgsql's integrated-auth support loads GSSAPI at runtime even when PostgreSQL
# uses password authentication. Keep the native dependency available so startup
# and future database authentication paths do not fail with a missing library.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# The chain registry reads deployment manifests from ./deployments relative to
# the working directory (/app). They live at the repo root, outside any project,
# so publish never picks them up; copy them explicitly from the build context.
# Without this the container finds no manifests and falls back to the addressless
# default chain stub. Env vars (ARTISANALBREW_*_MANIFEST) can still override.
COPY deployments/ ./deployments/

USER $APP_UID
ENTRYPOINT ["dotnet", "ThisCafeteria.Worker.dll"]
