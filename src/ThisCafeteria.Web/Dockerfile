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

RUN dotnet publish src/ThisCafeteria.Web/ThisCafeteria.Web.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# curl is not present in the base aspnet image; needed for the HEALTHCHECK below.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app/publish .

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD ["curl", "-f", "http://127.0.0.1:8080/health"]

USER $APP_UID
ENTRYPOINT ["dotnet", "ThisCafeteria.Web.dll"]
