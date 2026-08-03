# Pixel homepage production runbook

The homepage is deployed through the existing `deploy-azure` job in
`.github/workflows/ci.yml`. Merging to `main` builds immutable commit-SHA images
in ACR. Configure required reviewers on the GitHub `production` environment
before launch; once protected, that environment is the approval boundary before
any Azure mutation.

## Release gate

Before approving the environment:

1. Before merging the workflow change, apply the Bicep update that adds the
   `repo:AlejoReyna/ArtisanalBrew:environment:production` federated credential,
   then configure required reviewers on the GitHub `production` environment.
   The existing branch credential does not authenticate an environment-scoped
   job.
2. Confirm the PR contains the pixel homepage and its direct dependencies only.
   Worker, database migration, contract, and unrelated page changes do not
   belong in this release.
3. Require `build-test`, `crossstack-verification`, and
   `node tools/verify_pixel_crew.mjs` to pass.
4. Confirm desktop, mobile, keyboard, and `prefers-reduced-motion` browser
   checks have passed without console errors.
5. Record the candidate commit SHA. The workflow records the currently
   deployed Web and Worker images immediately before it updates either app.
6. Keep `main` quiet until the production job completes. Production runs queue
   instead of canceling one another because a canceled run could otherwise stop
   after applying a migration.

## Automated deployment contract

The workflow:

1. Classifies Web and Worker changes independently.
2. Builds and pushes only the affected images.
3. Applies EF migrations using the Key Vault connection string.
4. Updates Web first.
5. Requires readiness, the Blazor framework script, and—when `PixelHome.razor`
   is present—the server-rendered `.ph-hero` and `#ph-scene-root` markers.
6. Rolls Web back to its captured image automatically when those checks fail.
7. Updates Worker only after Web is healthy. A failed Worker update rolls both
   affected apps back so the release remains coherent.
8. Promotes successful SHA-tagged images to `latest` only after all affected
   apps succeed, preventing a later Bicep reconciliation from restoring a
   failed candidate.

## Post-deploy browser acceptance

Against `https://cafe.alexisreyna.dev/`, verify:

- The accessible heading is “Your next coffee, on-chain.”
- `.ph-scene--sim` is added after the Blazor circuit connects.
- `GEN 300` is selected and its description is visible.
- Switching between generation 0, 20, and 300 does not reset the field.
- Robots collect coins, show their collection pose, and emit the `+1`.
- Hiding and restoring the intro keeps the simulation running.
- Dragging a robot releases it back to policy control.
- With reduced motion enabled, the trained runtime does not start and the
  static composition remains complete.
- `/staking`, `/products`, `/story`, and `/procurement-lab` still navigate and
  render normally.

Watch Container App logs, readiness, restart count, HTTP 5xx responses, and
Blazor disconnects for at least 30 minutes.

## Manual rollback

Automatic rollback covers failed deployment checks. For a regression found
after acceptance:

1. Read the previous immutable image from the production job's “Capture
   currently deployed images” output.
2. Update `thiscafeteria-prod-web` to that exact SHA-tagged image. Never use
   `latest` for rollback.
3. Wait for `/health/ready` and `/_framework/blazor.web.js`.
4. Verify the homepage and critical routes.
5. Revert the release commit on `main` so source and production converge.

The pixel homepage release must not include a database migration. That keeps
the previous Web image compatible with the production schema and makes this
rollback path safe.
