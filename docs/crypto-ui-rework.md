# Crypto-Native Wallet/Staking UI Rework — Handoff Notes

_Branch: `feature/crypto-ui-rework` · Started 2026-07-02 · Status: implemented, pending connected-wallet verification_

## Goal

Make the MetaMask/Sepolia surfaces (staking view, profile dashboard, nav wallet UI) read as a
crypto-native dashboard **without losing the coffeeteria editorial style**, and give recruiters an
easy way to fund their wallets with Sepolia ETH so they can try the demo.

Design decisions locked with the owner:

- **Visual direction:** cream base, crypto accents. Keep the light `#FAF9F5` / espresso `#2C1B12` /
  sand `#EFE9DF` / green `#47614e` palette, Playfair Display + Inter, hairline borders and pill
  buttons — and add crypto furniture on top (chain badge, address chip, status dot, stat tiles,
  monospace/tabular numerals).
- **Faucet:** external links only, Sepolia ETH only. No backend faucet code, no hot wallet.
- **Reach:** YieldPanel + Profile + NavMenu (pill, user menu, wallet modal, drawer stats) + replace
  every `window.alert()` / `location.reload()` transaction flow with inline status.

## What was built

### Shared primitives — `src/ThisCafeteria.Web/Components/Shared/`

| Component | Purpose |
|---|---|
| `ChainBadge.razor` | Pill with status dot + "Ethereum Sepolia · 11155111". Dot pulses when `Connected`. |
| `AddressChip.razor` (+ collocated `.razor.js`) | Truncated mono address (`0x9D53…ceB`), copy-to-clipboard with checkmark feedback, optional explorer ↗ link. |
| `StatTile.razor` | Uppercase eyebrow label + tabular-num value + optional hint (e.g. USD estimate); shimmer skeleton when `Loading`. |
| `FaucetPanel.razor` | "Get free Sepolia ETH" — four faucet link cards (Google Cloud, Alchemy, Chainstack, QuickNode) with per-faucet requirements, the user's address chip, and a refresh-balance callback. |
| `TxStepper.razor` | Transaction stepper (per-step spinner/check/cross, "View tx ↗" etherscan links, error + dismiss). Also owns the `TxStepper.Model` state class. |

Global tokens in `wwwroot/app.css`: `--crypto-mono` (system mono stack — no font download),
`--crypto-positive`, `.tabular`, `.crypto-dot` (+`--idle`, `--pulse`), and the `crypto-pulse`,
`crypto-spin`, `crypto-shimmer` keyframes (defined once globally because Blazor CSS isolation does
not rewrite keyframe names).

### YieldPanel (`Components/Shared/YieldPanel.razor`)

- Hero: wallet-identity bar (ChainBadge + AddressChip) under the "Token Allocations" headline.
- 4-tile stat row: Staked / Pending Rewards (green accent) / CAFE balance / ETH balance with USD
  hint; skeletons while syncing (the old "Syncing…" strings are gone). Sidebar `<dl>` slimmed to
  Wallet Value (USD) + COFFEE balance + the address chip.
- Stake/unstake cards: "Available / Staked" context lines, MAX buttons wired via `data-max` /
  `data-target` attributes (rendered by Razor so re-renders keep them fresh — do not move these
  into JS closures), inline validation errors, tabular inputs.
- Recent Activity restyled: mono amounts, green dotted "Confirmed" pill, mono `Tx ↗`.
- Not-configured state is now a styled card (dev runs with `StakingPoolContract: ""` by design —
  see `docs/sepolia-staking-pool-deploy.md`).
- FaucetPanel renders (a) under the disconnected connect card, and (b) when connected, as a
  `<details>` section that auto-expands when ETH balance < `LowGasThresholdEth` (0.005).

### Transaction status (replaces alert/reload)

`wwwroot/js/coffeeStaking.js` no longer contains any `window.alert` or `location.reload`.

- `initCoffeePurchases(config, dotNetRef)` now takes a `DotNetObjectReference` (created/disposed by
  YieldPanel — the first `[JSInvokable]` usage in the app).
- JS `notify(flow, step, status, txHash, message)` → `[JSInvokable] OnTxStatusChanged` on
  YieldPanel; flows: `stake` (approve → stake → record), `unstake` (unstake → record), `purchase`
  (dormant `.btn-buy-token` path, no UI surface). `validate` steps surface as inline input errors.
- MetaMask rejection (`error.code === 4001`) maps to "Transaction rejected in MetaMask."
- On completion JS calls `OnTxCompleted` → `LoadDashboardAsync()` refreshes balances in place.
- The `/staking/api/record-stake|record-unstake` server verification flow is untouched.

### WalletDashboardState (`Services/Blockchain/WalletDashboardState.cs`)

Scoped (per-circuit) cache of `CoffeeDashboardModel`. NavMenu loads it lazily **only when the
drawer opens** (60s max age, 10s timeout, "—" while unknown); YieldPanel `Publish`es after every
successful dashboard load so drawer values are instant if the user visited `/staking`. Registered
in `Program.cs`.

### NavMenu + Profile

- Wallet pill: pulsing green dot + mono address. User menu: AddressChip + compact ChainBadge under
  the "Wallet" eyebrow (dialog now uses `aria-label`, the old `user-menu-brand-title` h2 is gone).
- Wallet modal: compact ChainBadge under "Connect a wallet" (behavior of `loginWithWallet`
  unchanged). Drawer header: dot + mono address; drawer stats now real (see above).
- Profile Identity & Network: AddressChip, ChainBadge (connected when `WalletChainId` matches
  config), "Get Sepolia ETH →" link to `/staking`; order rows use mono tx chips + tabular amounts.
  `PersistentComponentState` logic untouched.

## Verified

- `dotnet build` clean (the 2 warnings are pre-existing in `Concept.razor` / `Checkout.razor`).
- Browser pass (anonymous): `/staking` disconnected state + faucet panel, wallet modal with chain
  badge, mobile drawer, `/journal` transparent-header pill — all good, no console errors.
- `grep window.alert\|location.reload wwwroot/js/coffeeStaking.js` → zero hits.

## Remaining work (next session)

1. **Connected-wallet visual pass** (needs MetaMask): stat tiles with real balances, address-chip
   copy buttons, drawer stats after opening, Profile identity chips.
2. **Stepper smoke test**: set a temporary `StakingPoolContract` in
   `appsettings.Development.json`, attempt a stake — empty amount should show an inline error (no
   alert); rejecting in MetaMask should show the stepper error state; approving a dust amount
   should confirm step 1 with an etherscan link. Revert the temp value afterwards.
3. **Deploy the real staking pool** (`contracts/CafeStakingPool.sol`, steps in
   `docs/sepolia-staking-pool-deploy.md`) to exercise the full stake → record → refresh loop.
4. Optional polish: `<details open>` for the faucet is re-evaluated on every render, so a re-render
   can collapse a manually-opened panel while balance ≥ threshold; make it stateful if it annoys.
5. Faucet card amounts (0.05/0.1/0.5 ETH per day) were accurate as of 2026-07 — recheck
   occasionally, faucet policies drift.

## Explicitly out of scope here

The AWS → Azure migration (Infrastructure services, csproj package swaps, Dockerfiles, compose,
Worker, appsettings) is in flight on the same working tree and handled separately — none of those
files are part of this branch's commit.
