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

COPY --from=build /app/publish .

USER $APP_UID
ENTRYPOINT ["dotnet", "ThisCafeteria.Worker.dll"]
