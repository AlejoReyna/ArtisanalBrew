# This Cafeteria Doesn't Exist

ASP.NET reconstruction of the original coffee storefront, prepared as a clean .NET 10 solution with Blazor, ASP.NET Identity, PostgreSQL, Entity Framework Core, Docker Compose, background processing, and AWS integration placeholders.

## Tech Stack

- .NET 10 LTS and ASP.NET Core
- Blazor Web App with interactive server rendering
- Clean Architecture: Web, Application, Domain, Infrastructure, Worker
- Entity Framework Core with PostgreSQL
- ASP.NET Core Identity
- Serilog
- xUnit, FluentAssertions, Moq, WebApplicationFactory, Testcontainers
- AWS-ready service boundaries for S3, SQS, and SES

## Architecture

- `src/ThisCafeteria.Domain`: entities and enums.
- `src/ThisCafeteria.Application`: DTOs, validation, service interfaces, application services, repository contracts.
- `src/ThisCafeteria.Infrastructure`: EF Core DbContext, entity configurations, repositories, seed data, Identity user, AWS placeholders.
- `src/ThisCafeteria.Web`: Blazor pages, API controllers, Identity, Swagger, health checks.
- `src/ThisCafeteria.Worker`: background service placeholder for order processing via SQS.
- `tests`: unit and integration test projects.

The legacy Next.js app remains untouched at `/Users/alexis/TCDE/thisCafeteriaDoesntExist`.

## Run Locally

Postgres is published on host port **5433** (container 5432) so it does not conflict with a local PostgreSQL install on 5432.

```bash
cd /Users/alexis/TCDE/ThisCafeteria
cp .env.example .env
# Edita .env y reemplaza todos los valores CHANGE_ME / YOUR_DB_*
docker compose up -d postgres
dotnet restore
dotnet run --project src/ThisCafeteria.Web
```

Swagger is available in Development at `/swagger`, and health checks are exposed at `/health`.

The app requires `ConnectionStrings__DefaultConnection` from environment variables (`.env` for local development). No default password is embedded in source code.

## Migrations

```bash
dotnet tool install --global dotnet-ef
dotnet ef migrations add InitialCreate --project src/ThisCafeteria.Infrastructure --startup-project src/ThisCafeteria.Web
dotnet ef database update --project src/ThisCafeteria.Infrastructure --startup-project src/ThisCafeteria.Web
```

Admin user seeding reads:

- `Authentication:AdminEmail`
- `Authentication:AdminPassword`

## Tests

```bash
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
```

## Worker

```bash
dotnet run --project src/ThisCafeteria.Worker
```

The worker currently logs a simulated SQS polling loop. Real message consumption can be wired through `ISqsMessagePublisher` and AWS SDK clients in Infrastructure.

## AWS Roadmap

- Add typed AWS options and validate configuration at startup.
- Replace placeholder S3/SQS/SES services with real SDK-backed implementations.
- Store receipt PDFs/assets in S3.
- Publish order-processing messages to SQS after checkout.
- Send receipt emails through SES.
- Add infrastructure-as-code for dev/staging/prod environments.
