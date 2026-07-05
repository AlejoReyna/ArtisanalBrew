# Legacy AWS Infrastructure

This project originally ran on AWS. It has since been fully migrated to Azure
(Container Apps, Postgres Flexible Server, Blob Storage, Service Bus, Key
Vault, Communication Services Email) — see the "Azure Infrastructure" section
in the [README](../README.md) for the current architecture and
[`docs/azure-migration-plan.md`](azure-migration-plan.md) for the full
phase-by-phase migration log (Phase 0 audit through Phase 6 cutover).

## What the AWS setup was

- **Compute:** a single EC2 instance running the published app directly under
  `systemd`, deployed via SSH/SCP from GitHub Actions (`deploy-ec2` job).
- **Database:** AWS RDS for PostgreSQL (`*.rds.amazonaws.com`, `us-east-1`).
- **Wallet-status queue:** AWS SQS (`wallet-status`), published to directly
  from `SqsMessagePublisher`.
- **Checkout receipts:** AWS S3 (PDF upload) + AWS SES (email), called
  directly from `ReceiptService`.

## Where it lives now

None of this is in the live traffic path anymore — `cafe.alexisreyna.dev`
resolves to Azure Container Apps, and `.github/workflows/ci.yml` no longer has
an EC2 deploy job. The original AWS-era code, config, and docs are preserved
as-is on the **[`aws_legacy`](https://github.com/AlejoReyna/ArtisanalBrew/tree/aws_legacy)** git branch (a
snapshot taken right before this cleanup), including:

- `publish-ec2/` — the self-contained publish output used for the old
  SSH/SCP deploy to EC2.
- `docs/github-actions-ec2-deploy.md` — troubleshooting notes for the old
  EC2 security-group/SSH deploy path.
- `docs/aws-wallet-status.md` — the original SQS/RDS wallet-status design doc.

Check out that branch if you need to reference the exact pre-migration state.

## Naming note

A few AWS-flavored names are still live in current code even though they're
now backed by Azure services — this was a deliberate choice during the
migration, not leftover legacy code:

- `IS3StorageService` / `S3StorageService` → implemented with Azure Blob
  Storage.
- `IEmailSender` / `SesEmailSender` → implemented with Azure Communication
  Services Email.
- `ISqsMessagePublisher` / `SqsMessagePublisher` → implemented with Azure
  Service Bus.
- `WalletStatusEvent.AwsMessageId` / `PublishedToAwsAtUtc` /
  `MarkPublishedToAwsAsync` → same concept, now populated from Service Bus.

These were kept because `ISqsMessagePublisher` is consumed directly outside
`Infrastructure` (`WalletStatusController`, `WalletAuthController`), so
renaming would ripple across the codebase for a purely cosmetic reason. See
the "Phase 3" section of `docs/azure-migration-plan.md` for the full
reasoning.
