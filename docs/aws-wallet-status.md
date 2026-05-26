# AWS Wallet Status

This project is an ASP.NET Core app with Blazor Server/Razor Components plus API controllers. The frontend must call the backend only; PostgreSQL and SQS stay behind the .NET API.

## Runtime Flow

```text
Frontend wallet/login UI
  -> .NET backend API
  -> PostgreSQL RDS: wallet_status_events
  -> AWS SQS: wallet-status
```

The backend stores each status event first, then publishes the same status to SQS. AWS credentials are resolved by the AWS SDK from the local AWS CLI login, environment, or an IAM role. Do not put access keys in code.

## AWS Values

Use these non-secret values for development:

```env
AWS_REGION=us-east-1
AWS_PROFILE=<optional local AWS CLI profile>
SQS_QUEUE_URL=https://sqs.us-east-1.amazonaws.com/419197236352/wallet-status
DB_HOST=thiscafeteria.ce3wcicu69fo.us-east-1.rds.amazonaws.com
DB_PORT=5432
DB_NAME=thiscafeteria
DB_USERNAME=appuser
DB_PASSWORD=<local secret only>
```

The app builds the Npgsql connection string from `DB_*` if `ConnectionStrings__DefaultConnection` is not present. SSL is required for RDS:

```text
SSL Mode=Require;Trust Server Certificate=true
```

## Database Design

```sql
CREATE TABLE wallet_status_events (
    id uuid PRIMARY KEY,
    wallet_address text NOT NULL,
    status text NOT NULL,
    event_type text NULL,
    payload_json jsonb NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    published_to_aws_at timestamptz NULL,
    aws_message_id text NULL
);

CREATE INDEX ix_wallet_status_events_wallet_created_at
    ON wallet_status_events (wallet_address, created_at);

CREATE INDEX "IX_wallet_status_events_status"
    ON wallet_status_events (status);

CREATE INDEX "IX_wallet_status_events_aws_message_id"
    ON wallet_status_events (aws_message_id);
```

## API

Create/publish a status:

```http
POST /api/wallet-status
Content-Type: application/json

{
  "walletAddress": "0x0000000000000000000000000000000000000000",
  "status": "Connected",
  "eventType": "wallet-login.connected",
  "payload": {
    "source": "frontend"
  }
}
```

Get latest status:

```http
GET /api/wallet-status/{walletAddress}
```

Wallet login also writes to the same table and SQS flow through `/api/wallet-auth/challenge`, `/api/wallet-auth/verify`, and `/api/wallet-auth/logout`.

## Local Secrets

Preferred for local development:

```bash
cd /Users/alexis/TCDE/ThisCafeteria/src/ThisCafeteria.Web
dotnet user-secrets init
dotnet user-secrets set "DB_HOST" "thiscafeteria.ce3wcicu69fo.us-east-1.rds.amazonaws.com"
dotnet user-secrets set "DB_PORT" "5432"
dotnet user-secrets set "DB_NAME" "thiscafeteria"
dotnet user-secrets set "DB_USERNAME" "appuser"
dotnet user-secrets set "DB_PASSWORD" "<your local DB password>"
dotnet user-secrets set "AWS_REGION" "us-east-1"
dotnet user-secrets set "AWS_PROFILE" "<your aws cli profile>"
dotnet user-secrets set "SQS_QUEUE_URL" "https://sqs.us-east-1.amazonaws.com/419197236352/wallet-status"
```

Alternative with repo-root `.env`:

```env
DB_HOST=thiscafeteria.ce3wcicu69fo.us-east-1.rds.amazonaws.com
DB_PORT=5432
DB_NAME=thiscafeteria
DB_USERNAME=appuser
DB_PASSWORD=<your local DB password>
AWS_REGION=us-east-1
AWS_PROFILE=<your aws cli profile>
SQS_QUEUE_URL=https://sqs.us-east-1.amazonaws.com/419197236352/wallet-status
```

## Migrations

Apply the included migration:

```bash
cd /Users/alexis/TCDE/ThisCafeteria
dotnet ef database update \
  --project src/ThisCafeteria.Infrastructure \
  --startup-project src/ThisCafeteria.Web
```

If you prefer regenerating migrations later:

```bash
dotnet ef migrations add AddWalletStatusEvents \
  --project src/ThisCafeteria.Infrastructure \
  --startup-project src/ThisCafeteria.Web
```

## Curl Test

Use a valid Ethereum-style wallet address:

```bash
curl -X POST http://localhost:5000/api/wallet-status \
  -H "Content-Type: application/json" \
  -d '{
    "walletAddress":"0x0000000000000000000000000000000000000000",
    "status":"Connected",
    "eventType":"wallet-login.connected",
    "payload":{"source":"curl"}
  }'
```

Then read the latest status:

```bash
curl http://localhost:5000/api/wallet-status/0x0000000000000000000000000000000000000000
```

Check SQS:

```bash
aws sqs receive-message \
  --region us-east-1 \
  --queue-url https://sqs.us-east-1.amazonaws.com/419197236352/wallet-status \
  --max-number-of-messages 1 \
  --wait-time-seconds 5
```

Do not delete the message while debugging unless you intentionally want to consume it.

## Frontend

The Blazor app is same-origin, so it does not need a `NEXT_PUBLIC_API_BASE_URL`. A small browser client exists at:

```text
src/ThisCafeteria.Web/wwwroot/js/walletStatusApi.js
```

It calls the backend only:

```js
publishWalletStatus(walletAddress, status, eventType, payload)
getLatestWalletStatus(walletAddress)
```

If the frontend is deployed separately later, set `window.thisCafeteriaApiBaseUrl` before importing that module. Do not expose AWS or RDS credentials to the browser.
