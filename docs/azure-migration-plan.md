# Azure Migration Plan — Phase 0 Audit

This document records what was actually verified in the repo before any infrastructure work started. Several assumptions in the original migration prompt were **wrong or incomplete** — see "Corrections" below. Do not proceed to Phase 1 until the open questions are resolved with the repo owner.

## What was verified

### Deploy path (confirmed as described)
- `.github/workflows/ci.yml` has two jobs: `build-test` (restore/build/test/format) and `deploy-ec2` (needs `build-test`, runs only on push to `main`).
- `deploy-ec2` publishes `ThisCafeteria.Web` self-contained for `linux-x64`, builds an EF Core migrations bundle, SCPs a tarball to the EC2 host, and over SSH: stops `systemctl thiscafeteria`, unpacks to `/opt/thiscafeteria`, runs the migration bundle with env sourced from `/etc/thiscafeteria/thiscafeteria.env`, restarts the service as `www-data`, and curls `/health`.
- Auth is a long-lived `EC2_SSH_PRIVATE_KEY` GitHub secret plus `EC2_HOST`/`EC2_USER`/`EC2_SSH_PORT`. No OIDC/federation in use today.
- `docs/github-actions-ec2-deploy.md` confirms the EC2 security group historically only allowed SSH from one developer IP — a known recurring failure mode for the runner.

### Postgres (corrected — see below)
- `docker-compose.yml` runs `postgres:16-alpine` + pgadmin, port **5433 → 5432**, for **local development only**. Confirmed local-only by README ("Run Locally" section).
- **Production Postgres is AWS RDS**, not self-hosted on the EC2 box: `docs/aws-wallet-status.md` gives the real endpoint `thiscafeteria.ce3wcicu69fo.us-east-1.rds.amazonaws.com`, `appuser`, SSL-required connection. `DatabaseConnectionStringFactory.cs` builds the Npgsql connection string from `DB_HOST/DB_NAME/DB_USERNAME/DB_PASSWORD/DB_PORT` env vars with `SslMode.Require` when `ConnectionStrings__DefaultConnection` isn't set directly — consistent with an RDS-style managed Postgres target.
- This means Phase 5 (data migration) is a real production data migration (orders, identity users, wallet_status_events, products, etc.), not a toy/local dataset.

### AWS SDK usage — **not a uniform placeholder** (major correction)
The prompt states "AWS SDK (S3, SQS, SES) are placeholders — interfaces exist but nothing is wired up." That is only true for **half** of the AWS surface:

| Component | File | Status |
|---|---|---|
| `IS3StorageService` / `S3StorageService` | `Infrastructure/Services/S3StorageService.cs` | **True placeholder.** Logs and returns a fake `s3://placeholder/...` URL. Registered in DI but **never injected anywhere** in Web/Application — dead code. |
| `IEmailSender` / `SesEmailSender` | `Infrastructure/Services/SesEmailSender.cs` | **True placeholder.** Logs only, no SES call. Also registered in DI but **never injected anywhere** — dead code. |
| `ISqsMessagePublisher` / `SqsMessagePublisher` | `Infrastructure/Services/SqsMessagePublisher.cs` | **Real, live integration.** Uses `AmazonSQSClient` to actually call `SendMessageAsync`. Consumed by `WalletStatusController` and `WalletAuthController` for the wallet login/status feature, publishing to a real production SQS queue (`docs/aws-wallet-status.md`: `https://sqs.us-east-1.amazonaws.com/419197236352/wallet-status`). Fails soft (logs a warning and returns null) if unconfigured, but is not a stub — it's production traffic. |
| `IReceiptService` / `ReceiptService` | `Infrastructure/Services/ReceiptService.cs` | **Real, live integration.** Directly injects the concrete `AmazonS3Client` and `AmazonSimpleEmailServiceV2Client` (bypassing the placeholder interfaces above) to generate a receipt PDF (QuestPDF), `PutObjectAsync` it to S3, and `SendEmailAsync` it via SES on every checkout. Consumed from `Checkout.razor`. This is a real checkout-critical path today. |
| `ThisCafeteria.Worker` / `OrderProcessingWorker` | `Worker/OrderProcessingWorker.cs` | **Placeholder.** Logs "SQS polling placeholder: no messages consumed yet" every 30s. No real consumption. Points at a different queue name (`order-processing`, via a local LocalStack URL) than the wallet-status queue — this queue does not appear to exist in production. |

**Practical implication for Phase 3:** this is not "implement 3 empty interfaces." It's:
1. Migrate the **real** wallet-status publish path (`SqsMessagePublisher`) to Azure Service Bus.
2. Migrate the **real** receipt path (`ReceiptService`'s S3 upload + SES email) to Azure Blob Storage + an Azure email provider — this is a checkout-path change and needs care, and should probably be refactored to go through `IS3StorageService`/`IEmailSender` rather than holding concrete Azure clients directly, fixing the layering the AWS version skipped.
3. Decide what to do with the orphaned `IS3StorageService`/`IEmailSender` placeholders — either delete them (dead code) or make them the real abstraction `ReceiptService` calls through (recommended, keeps Infrastructure the only layer that changes).
4. Decide whether `OrderProcessingWorker`'s simulated loop becomes a real Service Bus consumer as part of this migration or stays a placeholder — out of scope of the original AWS Roadmap, so flagging as a question rather than assuming.

### Containerization
- No `Dockerfile` exists anywhere in the repo (checked recursively, excluding `bin`/`obj`). Phase 1 is greenfield — nothing to reconcile with an existing image.
- `global.json` pins SDK `10.0.300` (`rollForward: latestFeature`) — Dockerfile build stage should match.
- `ThisCafeteria.Worker` is a real separate deployable (`Microsoft.NET.Sdk.Worker`, its own `appsettings.json`), so it needs its own Dockerfile per the prompt's Phase 1 instruction, even though its current logic is a placeholder.

### Web3 / Sepolia (confirmed untouched, out of scope)
- `contracts/CafeStakingPool.sol` and `docs/sepolia-staking-pool-deploy.md` confirm a deployed `CafePaymentToken`/`CoffeeCoin` pair and staking pool on Ethereum Sepolia (chain ID 11155111), configured via `Blockchain__Network__*` settings and a `CoffeeCoinOwner__PrivateKey` secret. This is application-level config (env vars), not infrastructure — no Azure equivalent needed, and it will simply carry over as env vars into the new Container App / Key Vault.

### Housekeeping notes
- `docs/environment-variables.md` is written in Spanish and describes the EC2 env-file workflow already covered above — no new information beyond confirming `DB_*` var names and the `/etc/thiscafeteria/thiscafeteria.env` convention.
- Two untracked prompt files already exist at repo root: `AZURE_MIGRATION_PROMPT.md` and `EC2_RECOVERY_PROMPT.md` — worth checking these aren't stale duplicates of this effort before Phase 1.
- No domain/DNS configuration lives in this repo (no Route 53, no DNS-as-code). `cafe.alexisreyna.dev` cutover (Phase 6) will need to be done manually wherever DNS is actually managed — **this needs to be confirmed with the repo owner**, it cannot be verified by reading the repo.

## Corrections to the original prompt (summary)

1. **"AWS SDK (S3, SQS, SES): these are placeholders"** — false for SQS (wallet-status) and for the S3+SES calls inside `ReceiptService`. True only for the orphaned `IS3StorageService`/`IEmailSender`/`SesEmailSender` classes. Migration must treat the wallet-status and checkout-receipt paths as real production traffic with real cutover risk, not greenfield implementation.
2. **"Verify yourself where Postgres actually lives in production"** — confirmed: AWS RDS, not self-hosted on EC2, not the docker-compose instance.
3. Worker project exists and needs containerizing per Phase 1, but its business logic is inert — containerizing it is mostly a "prove it can run in Container Apps too" exercise unless the user wants real Service Bus consumption wired in as part of this migration.

## Decisions (resolved 2026-07-01)

1. **Worker scope:** `OrderProcessingWorker` will get a **real** Azure Service Bus consumer as part of this migration (Phase 3), not just a container wrapper around the simulated loop.
2. **Placeholder interfaces:** `IS3StorageService`/`IEmailSender` will be **repurposed**, not deleted. `ReceiptService` will be refactored to call through these interfaces (backed by `Azure.Storage.Blobs` / the chosen email provider) instead of holding concrete SDK clients directly — this fixes the layering violation in the current AWS implementation and gives Infrastructure a single real abstraction for storage and email.
3. **DNS for `cafe.alexisreyna.dev`:** managed in **AWS Route 53**. Phase 6 cutover will need a Route 53 record change (CNAME/ALIAS to the Container Apps custom domain verification + eventual A/CNAME to the app's ingress FQDN) — this stays in Route 53, it does not move to Azure DNS as part of this migration.

## Phase 1 — Containerize (done, verified locally 2026-07-02)

- `src/ThisCafeteria.Web/Dockerfile` and `src/ThisCafeteria.Worker/Dockerfile`: multi-stage, SDK `10.0` build → runtime final stage, non-root `$APP_UID`, build context is the repo root (`docker build -f src/ThisCafeteria.Web/Dockerfile .`).
- `docker-compose.yml` gained `web` and `worker` services for local verification: they `env_file: .env` (reusing the existing local dev config) and override only `ConnectionStrings__DefaultConnection` to point at the `postgres` service (`Host=postgres;Port=5432;...`) instead of the host-mapped `localhost:5433` used for bare-metal `dotnet run`.
- **Finding:** `ThisCafeteria.Worker` cannot run on the plain `mcr.microsoft.com/dotnet/runtime:10.0` image — it fails at launch needing `Microsoft.AspNetCore.App`. Root cause: `ThisCafeteria.Infrastructure` (which both Web and Worker depend on) references `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, which pulls in an implicit `FrameworkReference` to the ASP.NET Core shared runtime for every consumer, regardless of that consumer's own SDK type. Fixed by using `mcr.microsoft.com/dotnet/aspnet:10.0` as the Worker's final base image too (documented inline in the Dockerfile). No source changes were made to fix this — purely a base-image choice.
- Verified: `docker compose build web worker` succeeds; `docker compose up -d postgres web worker` starts cleanly; Web runs EF Core migrations against the compose `postgres` service, seeds 30 catalog products, and serves `GET /health` → `200 Healthy` from the host on port 8080; Worker starts its (still-placeholder) background loop without errors.
- Added `.dockerignore` at repo root to keep `bin/`, `obj/`, `publish-ec2/`, and docs out of the build context.

## Remaining open questions (revisit before Phase 5/6, not blocking Phase 1)

1. Confirm you're okay with the wallet-status feature briefly losing its queue (or running against two queues during cutover) during the SQS → Service Bus swap — this is live functionality, not a stub.
2. Any existing data in the production RDS instance that has retention/compliance concerns for the `pg_dump`/`pg_restore` migration (Phase 5)?

## Cost decisions (resolved 2026-07-02)

Estimated realistic total: **~$20–25/month** against the $100 Azure for Students credit (~4–5 months runway if everything runs continuously; the credit renews yearly with a university email).

| Resource | SKU | Est. cost | Decision |
|---|---|---|---|
| Azure Database for PostgreSQL Flexible Server | Burstable B1ms, 32GB, **always-on** | ~$15/month | Confirmed: run continuously, not stop/start. Priority is the app being reachable any time for recruiters/interviewers over saving cost. B1ms is the floor — there is no cheaper Flexible Server SKU, and cheaper alternatives (self-hosted Postgres in a container, third-party free-tier Postgres) were rejected because they undermine the "Azure Databases" competency this project exists to demonstrate. |
| Azure Container Registry | Basic | ~$5/month | Confirmed: use ACR over free GitHub Container Registry, to keep the managed-identity `AcrPull` role-assignment story fully within Azure IAM (Phase 2 deliverable) rather than a GitHub-token-based pull credential. |
| Container Apps (consumption) | 0.5 vCPU / 1GiB, scale-to-zero | ~$0–5/month | Default: scale-to-zero for the Web app unless the user later says otherwise. |
| Service Bus, Storage, Key Vault, ACS Email | Basic/Standard, pay-per-use | < $1/month each | No decision needed, negligible at demo volume. |

**Action item before spending anything:** set an Azure budget alert (e.g. at $20/$50/$80) as a safety net — not yet done, should happen alongside or before the first `az deployment`.

Phase 1 (Dockerfiles for Web and Worker) is done — see above.

## Phase 2 — Infrastructure as code (written, not yet deployed)

`infra/main.bicep` (subscription scope, creates the resource group) orchestrates modules under `infra/modules/`: `managedIdentity`, `containerRegistry`, `logAnalytics`, `containerAppsEnvironment`, `postgres`, `storage`, `serviceBus`, `keyVault`, and a reusable `containerApp` module used once each for Web and Worker. `infra/main.bicepparam` is the example parameters file — it pulls secrets from environment variables via `readEnvironmentVariable()` so nothing sensitive is ever committed.

Key decisions baked into the templates:
- **Single User-Assigned Managed Identity** shared by both Container Apps, with role assignments: `AcrPull` on the registry, `Key Vault Secrets User` on the vault, `Storage Blob Data Contributor` on the storage account, and both `Azure Service Bus Data Sender`/`Data Receiver` on the namespace (both apps get both roles since Web publishes wallet-status and Worker will consume order-processing — simpler than splitting per-app roles for a two-container demo).
- **Container Apps Environment is Consumption-only** (no custom VNet, no dedicated workload profile) to stay on the cheapest billing model. Trade-off: Postgres is reached over its public endpoint (with an `AllowAzureServices` firewall rule, SSL enforced), not a private endpoint — documented inline in `postgres.bicep` as a deliberate cost/complexity call, not an oversight.
- **Service Bus Basic tier**, two queues: `wallet-status` (replaces the AWS SQS queue) and `order-processing` (for the Worker's real consumer, per the earlier decision to not leave it a placeholder).
- **Key Vault stores** `db-connection-string` (built from the Postgres module's output FQDN + the admin credentials) and, only if a non-empty password is supplied, `authentication-admin-password`. Both are wired into the Container Apps via native Key Vault secret references (`secretRef` + the shared managed identity) rather than plain env vars.
- **Blockchain/Web3 config and the `CoffeeCoinOwner__PrivateKey` wallet key are deliberately NOT in any Bicep file or parameters file.** Per the guardrail not to touch Sepolia/wallet configuration, `main.bicep` exposes `additionalWebEnvVars`/`additionalWorkerEnvVars` array parameters so this config can be injected at deploy time from the existing local `.env`, without ever being written into version control.
- **Azure Communication Services Email is intentionally not provisioned here** — the original phase breakdown's Phase 2 resource checklist doesn't include it, and it makes more sense to provision it alongside the actual email-sending code in Phase 3, not before.
- **`webImage`/`workerImage` default to a public placeholder image** (`mcr.microsoft.com/dotnet/samples:aspnetapp`) so the infrastructure can be provisioned standalone before CI/CD (Phase 4) has ever pushed a real image to the (initially empty) ACR — the deploy pipeline is expected to overwrite this via `az containerapp update --image ...`.
- Fixed one real naming-constraint bug while writing this: Key Vault names are capped at 24 characters — the other resources' naming scheme (`project-env-xx-token`) would have produced a 35-character name. Key Vault uses a shorter `tc-kv-<token>` scheme instead.

**Validated (2026-07-02):** installed Azure CLI 2.87.0 (Homebrew) and Bicep CLI 0.44.1 (`az bicep install`), then ran `az bicep build --file infra/main.bicep` and `az bicep build-params --file infra/main.bicepparam`. Both compiled cleanly after fixing three real bugs the build caught:
1. `main.bicep`: a doubled single-quote (`app''s`) inside a `@description()` string — invalid escaping in Bicep (needs `\'`), which the parser read as two arguments to the decorator.
2. `keyVault.bicep`: `@secure()` cannot decorate a param of type `array` (only `object`/`string`) — changed the `secrets` param from an array of `{name, value}` to an object map (`{ secretName: secretValue }`), iterated via `items(secrets)`.
3. `keyVault.bicep`: the param `secretsReaderPrincipalId` tripped the `secure-secrets-in-params` linter rule (name-based heuristic, false positive — it's a principal ID, not a secret) — renamed to `keyVaultReaderPrincipalId`.

**Validated against the live subscription (2026-07-02):** ran `az login` — subscription is "Azure for Students" (`b789cae9-50d4-4b88-9bd1-02e8d512189c`), tenant `uanl.edu.mx`. Registered all required resource providers (`Microsoft.App`, `Microsoft.DBforPostgreSQL`, `Microsoft.ContainerRegistry`, `Microsoft.ServiceBus`, `Microsoft.KeyVault`, `Microsoft.OperationalInsights`, `Microsoft.ManagedIdentity`, `Microsoft.Storage`) — all were `NotRegistered` on this fresh subscription, now `Registered`.

**Found and fixed a real subscription-level deployment restriction:** `az deployment sub validate` (subscription-scope, creating the resource group inline as part of the same deployment) failed with `RequestDisallowedByAzure` on the managed identity, Log Analytics workspace, and Postgres Flexible Server specifically — "This policy maintains a set of best available regions where your subscription can deploy resources... contact support." This happened in every region tried (`eastus`, `eastus2`, `westus2`). But creating a resource group directly (`az group create`) and even a managed identity directly (`az identity create`) into an *already-existing* resource group both worked fine in the same regions — so the restriction is specific to subscription-scope deployments that create the RG inline, not a genuine regional/quota block on this subscription.

**Fix:** refactored `infra/main.bicep` from `targetScope = 'subscription'` to `targetScope = 'resourceGroup'`. The resource group is now created separately (`az group create`), and `main.bicep` deploys into it. This is also a more conventional pattern for CI/CD pipelines (create-RG-once, deploy-many-times) than the original subscription-scope-creates-everything design.

After the refactor: created `thiscafeteria-prod-rg` in `eastus2` (empty RG, no cost), then ran `az deployment group validate` against it with the real template + parameters file — **validation succeeded**, all 14 resources (Postgres Flexible Server + database + firewall rule, Managed Identity, Log Analytics, and 8 nested module deployments for ACR/Container Apps Environment/Key Vault/Service Bus/Storage/Web app/Worker app) passed. No `what-if`/actual deployment has been run yet — validation only checks template correctness and RBAC/policy eligibility, not full runtime behavior.

**Region decision:** using `eastus2` going forward (confirmed working for this subscription), not the originally-assumed `eastus`.

**Budget alert deployed (2026-07-02):** `infra/budget.bicep` (subscription-scope, notification-only, no cost) creates a `Microsoft.Consumption/budgets` resource `thiscafeteria-azure-for-students-budget`: $100/month, email alerts at 20%/50%/80% ($20/$50/$80) to `alberto.reyna@proton.me`. Deployed and confirmed live via `az consumption budget show`.

**First real deployment attempt (2026-07-02, `eastus2`) partially failed:** `az deployment group create` for `main.bicep` created the Managed Identity, Log Analytics workspace, Storage Account, Service Bus namespace, and ACR successfully, but failed on two resources with region-specific (not the general "allowed locations" policy) restrictions:
- Container Apps Environment: `MaxNumberOfEnvironmentsInSubExceeded` — "cannot create Container App Environments in region 'East US 2'."
- Postgres Flexible Server: `LocationIsOfferRestricted` — "Subscriptions are restricted from provisioning in location 'eastus2'."

Deployments in this ARM resource group are not transactional — partial resources persisted after the failure.

**Root cause found:** the subscription has an actual Azure Policy assignment (`sys.regionrestriction`, "Allowed resource deployment regions") with `listOfAllowedLocations = [southcentralus, canadacentral, westus, mexicocentral, eastus2]`. `eastus2` passes that general policy but is separately capacity-restricted for these two specific resource types on this account (a different, resource-level restriction, not caught by `az deployment group validate`, which only flagged the Container Apps one at preflight — the Postgres one only surfaced once actual provisioning was attempted). Cross-referencing each service's own supported-region list against the policy's allowed list: `mexicocentral` doesn't support Container Apps at all; `westus`, `canadacentral`, and `southcentralus` support both services and are in the allowed-locations policy.

**Fix:** deleted the partially-created `thiscafeteria-prod-rg` (all empty/no-data resources, no meaningful loss) and recreated it in `westus`. Changed the default `location` in both `main.bicep` and `main.bicepparam` from `eastus`/`eastus2` to `westus`, with an inline comment explaining why. Redeployed — see below for the outcome.

**Lesson for future Azure-for-Students work:** always check `az policy assignment list --disable-scope-strict-match` for the `sys.regionrestriction` assignment first, and don't assume a region that passes that general policy will work for every resource type — Postgres Flexible Server and Container Apps environments each have their own additional per-subscription-type regional capacity restrictions layered on top.

**`westus` deployment (2026-07-02): all 10 resources live and healthy.** (`mexicocentral` was considered instead, per the user's request, but ruled out — Azure Container Apps has no presence in that region at all yet, confirmed via `Microsoft.App`'s own resourceType location list, independent of any subscription restriction.) The first `westus` attempt reported a top-level `DeploymentFailed` on `workerApp` with `ResourceNotFound` — but both Container Apps had actually provisioned successfully underneath; this was a transient ARM read-back race after resource creation, not a real failure. Re-running the (idempotent) deployment produced a clean `Succeeded` end to end.

Verified directly (not just "exists"):
- Postgres Flexible Server: `state: Ready`, `version: 16`, `Standard_B1ms`.
- ACR: `provisioningState: Succeeded`, `adminUserEnabled: false` (identity-based pull only, as designed).
- Key Vault: `provisioningState: Succeeded`, RBAC authorization enabled.
- Service Bus namespace: `provisioningState: Succeeded`, `status: Active`.
- Managed identity has exactly the 5 intended role assignments (`AcrPull`, `Storage Blob Data Contributor`, `Key Vault Secrets User`, `Azure Service Bus Data Sender`, `Azure Service Bus Data Receiver`) at the correct resource scopes — confirmed via `az role assignment list`.
- Key Vault secret read as the logged-in CLI user correctly returned `Forbidden` — expected and correct, since only the managed identity (not even the deploying human user) was granted a Key Vault role, per least-privilege design.
- Web Container App (still running the placeholder image) responds `200 OK` over HTTPS at its auto-assigned FQDN (`thiscafeteria-prod-web.ashyground-75811a1d.westus.azurecontainerapps.io`) — confirms ingress, TLS, and DNS all work end-to-end at the infrastructure level, before any real application code is deployed.

**Not yet done (superseded below):** the Web/Worker Container Apps are still running the public placeholder image, not the real app. No real traffic, no data, no EF Core migrations against the new Postgres yet.

## Phase 3 — Replace the AWS placeholders (code written, image not yet pushed)

Per the earlier decisions (repurpose the placeholder interfaces, give the Worker real Service Bus consumption), rewrote the following in `ThisCafeteria.Infrastructure` and `ThisCafeteria.Worker` — **interface contracts (`IS3StorageService`, `IEmailSender`, `ISqsMessagePublisher`) were kept as-is or minimally extended, not renamed**, even though they're AWS-flavored names now backed by Azure services. Reasoning: `ISqsMessagePublisher` is consumed directly by `WalletStatusController`/`WalletAuthController` in the Web project, so renaming it would ripple outside Infrastructure for purely cosmetic reasons — not worth the blast radius. Domain-level AWS-flavored names (`AwsMessageId`, `PublishedToAwsAtUtc`, `MarkPublishedToAwsAsync` on `WalletStatusEvent`) were left untouched for the same reason plus needing a new EF migration — same conceptual purpose either way.

- **`IEmailSender`** (`Services/IEmailSender.cs`): extended from `SendAsync(to, subject, body)` to `SendAsync(OutboundEmail email)`, where `OutboundEmail` now carries an optional `IReadOnlyList<EmailAttachmentData>` — needed because the original SES implementation sent the receipt PDF as a raw MIME attachment, and the placeholder interface never supported that.
- **`S3StorageService.cs`** → now implements `IS3StorageService` against Azure Blob Storage (`Azure.Storage.Blobs`), uploading to the `receipts` container from `Azure:Storage:BlobEndpoint`/`ReceiptsContainerName` config, authenticated via the shared `TokenCredential`.
- **`SesEmailSender.cs`** → now implements `IEmailSender` against Azure Communication Services Email (`Azure.Communication.Email`), using a Key-Vault-backed connection string + `Azure:Communication:SenderAddress`.
- **`SqsMessagePublisher.cs`** → now implements `ISqsMessagePublisher` against Azure Service Bus (`Azure.Messaging.ServiceBus`) for the real wallet-status queue, preserving the original fail-soft contract (log + return null on failure, since `WalletStatusController` treats a null publish result as "stored but not published" rather than a hard error).
- **`ReceiptService.cs`** refactored to depend on `IS3StorageService`/`IEmailSender` abstractions instead of holding concrete `AmazonS3Client`/`AmazonSimpleEmailServiceV2Client` directly — fixes the layering violation the AWS version had (it bypassed the placeholder interfaces entirely). Same fail-fast behavior preserved: throws `InvalidOperationException` up front if blob endpoint or sender address aren't configured, since checkout receipts are business-critical.
- **`AzureClientFactory.cs`** (replaces `AwsClientFactory.cs`): builds a single `TokenCredential` via `DefaultAzureCredential`, using the configured managed identity client ID when present. This is what lets the exact same code authenticate via the Container App's managed identity in production *and* via `az login` locally for a developer — no separate local/prod branching needed.
- **`AzureOptions.cs`** (replaces `AwsMessagingOptions.cs`): nested config classes matching the `Azure:*` env vars already wired in `main.bicep` (`ManagedIdentity`, `Storage`, `ServiceBus`, `Communication`).
- **`ThisCafeteria.Worker/OrderProcessingWorker.cs`**: replaced the simulated 30-second logging loop with a real `ServiceBusProcessor` against the `order-processing` queue (per the earlier decision not to leave the Worker a placeholder). Nothing in the app publishes to this queue yet — there's no existing "place an order" flow wired to Service Bus — so today it's a real, idle consumer waiting for messages, not a fully round-tripped feature.
- Removed all `AWSSDK.*` NuGet packages (`Infrastructure` and the leftover unused reference in `Web`) and the now-unused `MimeKit` dependency; added `Azure.Identity`, `Azure.Storage.Blobs`, `Azure.Messaging.ServiceBus`, `Azure.Communication.Email`.
- Updated `appsettings.json`/`appsettings.Development.json` (Web and Worker) and `.env.example`: replaced the `AWS` config section with the new nested `Azure` section. Blockchain/Sepolia config in these same files was left completely untouched.
- Added `infra/modules/communicationEmail.bicep`: provisions `Microsoft.Communication/emailServices` + an `AzureManagedDomain` (no custom-domain verification needed, sends from an auto-generated `...azurecomm.net` address — fine for a demo) + `Microsoft.Communication/communicationServices`, wired into `main.bicep` as a new Key Vault secret (`azure-communication-connection-string`) and new env vars (`Azure__Communication__SenderAddress`, `Azure__ManagedIdentity__ClientId`).
- Verified: `dotnet build --configuration Release` (0 errors, same 2 pre-existing unrelated warnings as before this work), `dotnet test` (all 15 tests pass — no existing test touched any of the rewritten classes), `dotnet format --verify-no-changes` (clean, matches the CI format-check gate).
- Bicep re-validated (`az deployment group validate`) and redeployed against the live `thiscafeteria-prod-rg` to add the new ACS Email resources and updated Container App env vars/secrets.

**Bicep deploy attempt 1 (2026-07-02) failed on the new `communicationEmail` module:** `domainManagement: 'AzureManagedDomain'` on the `Microsoft.Communication/emailServices/domains` resource — a real bug, not a subscription restriction. The domain resource's *name* is conventionally `AzureManagedDomain`, but the `domainManagement` **property value** must be `'AzureManaged'`. Fixed, rebuilt, and redeployed — `provisioningState: Succeeded`, confirmed the `AzureManagedDomain` child resource now exists, the Web app still responds `200 OK`, and all 10 expected env vars (including the new `Azure__Communication__*`, `Azure__ManagedIdentity__ClientId`) are present on the Web Container App.

**Real images built, pushed, and deployed (2026-07-02) — Phase 3 fully verified end-to-end in production:**
- Re-verified locally first via `docker compose` (web+worker rebuilt with the new Azure code): DB migrations still apply, `/health` returns 200, and the wallet-status endpoint correctly logs `Service Bus publish skipped ... not configured` when Azure config is empty (local `.env` has no Service Bus namespace) — confirms the fail-soft path still works exactly as designed.
- Built both images for `linux/amd64` (the host is arm64; Container Apps expects amd64) via `docker buildx build --platform linux/amd64 ... --push`, authenticated to ACR via `az acr login` (uses the existing `az login` session, no separate credentials created), pushed as `:v1` and `:latest`.
- `az containerapp update --image ...` for both `thiscafeteria-prod-web` and `thiscafeteria-prod-worker` — new revisions (`--0000002`) came up `Healthy`/`Provisioned` for both.
- **Real app `/health` now returns `200 Healthy`** (previously the placeholder image's generic response).
- **Worker logs confirm a real Service Bus connection in production:** `"Order processing worker started, listening on queue order-processing"` — genuinely connected, not the "not configured" idle path.
- **Full round-trip verified against real Azure Service Bus:** POSTed to `/api/wallet-status` on the live app → response showed `"publishedToAws":true"` with a real message ID → confirmed via `az servicebus queue show` that the `wallet-status` queue actually has 1 active message. The AWS-flavored field names (`publishedToAws`, `awsMessageId`) are unchanged by design (see naming decision above) but the data is now flowing through Azure Service Bus for real.

Phase 3 is complete and running in production. Not yet done: Phase 4 (CI/CD rewrite for ACR + OIDC), Phase 5 (data migration from the old RDS instance, if wanted), Phase 6 (DNS cutover to `cafe.alexisreyna.dev` + managed cert), Phase 7 (EC2 decommission, only after a stable soak period).
