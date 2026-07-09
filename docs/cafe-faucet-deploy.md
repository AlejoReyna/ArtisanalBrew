# CAFE Faucet Deployment

There is no in-app way to mint or transfer CAFE to a new wallet — `CafePaymentToken`
was deployed externally and its source isn't in this repo, so only whoever holds
that token's supply can fund testers. This faucet solves that without giving the
app itself a private key: you fund the faucet contract once from your own wallet,
and after that any wallet can call `claim()` directly to get a fixed amount of
CAFE, gated by a per-address cooldown enforced on-chain.

Source file:

```text
contracts/CafeFaucet.sol
```

## Contract Behavior

```solidity
claim()                                   // sends `claimAmount` CAFE to msg.sender
nextClaimAt(address account) view         // unix timestamp the account can claim again (0 = never claimed)
canClaim(address account) view            // true if cooldown has passed and the faucet holds enough CAFE
claimAmount() view                        // current claim amount, in wei
cooldownSeconds() view                    // current cooldown, in seconds
setClaimAmount(uint256) onlyOwner
setCooldownSeconds(uint256) onlyOwner
rescueTokens(address recipient, uint256 amount) onlyOwner
```

Nothing about the claim amount or cooldown is hardcoded in the app — the Staking
page reads `claimAmount()`, `cooldownSeconds()`, `nextClaimAt()`, and `canClaim()`
live from the contract on every dashboard load, so changing them on-chain via
`setClaimAmount`/`setCooldownSeconds` is immediately reflected in the UI.

## Constructor Inputs

```text
initialOwner:      your wallet address
cafeToken_:        deployed CafePaymentToken address (0x15DbED39271D2788a9Be63ffB34C8E2DdED8754A)
claimAmount_:       100000000000000000000   (100 CAFE, 18 decimals)
cooldownSeconds_:   86400                    (24 hours)
```

## Deploy With Remix

Same path as the staking pool — this repo has no Hardhat/Foundry setup.

1. Open [Remix](https://remix.ethereum.org/).
2. Create a new file named `CafeFaucet.sol` and paste the contents of
   `contracts/CafeFaucet.sol`.
3. Solidity Compiler tab: compiler `0.8.20`+, optimization enabled, compile.
4. Deploy & Run Transactions tab:
   - Environment: `Injected Provider - MetaMask`.
   - Network in MetaMask: `Sepolia`.
   - Contract: `CafeFaucet`.
5. Deploy with the constructor arguments above, confirm in MetaMask.
6. Copy the deployed `CafeFaucet` address.

## Fund The Faucet

The faucet pays out of its own CAFE balance — it never mints. Send CAFE to the
deployed `CafeFaucet` address from whichever wallet holds it:

- Sepolia Etherscan → `CafePaymentToken` (`0x15DbED39271D2788a9Be63ffB34C8E2DdED8754A`) →
  **Contract → Write Contract**, connect the holder wallet, call
  `transfer(<CafeFaucet address>, <amount in wei>)`.
- Or send it directly from that wallet in MetaMask.

Fund it with enough for however many claims you expect (e.g. 5,000 CAFE covers
50 claims at the 100 CAFE default).

## Update App Configuration

```json
{
  "Blockchain": {
    "Network": {
      "CafeFaucetContract": "<CafeFaucet address>"
    }
  }
}
```

Environment variable equivalent:

```bash
export Blockchain__Network__CafeFaucetContract="<CafeFaucet address>"
```

Set it in `src/ThisCafeteria.Web/appsettings.json` (prod) or
`appsettings.Development.json` / user secrets (local), then restart the app.

## Verify In The App

1. Open `/staking` and connect a wallet that doesn't already hold CAFE.
2. A "Need test CAFE?" card appears above the faucet-for-ETH section, showing
   the live claim amount (reads `claimAmount()`).
3. Click **Claim CAFE**, confirm the transaction in MetaMask.
4. `CAFE Balance` updates once the transaction is recorded; the card now shows
   a "Next claim in ~24h" countdown (reads `nextClaimAt()`).
5. Stake the claimed CAFE from the same page to confirm the end-to-end flow.

## Important Notes

- No server-side private key is involved — every claim is a transaction signed
  by the claiming wallet itself, so there's nothing new to add to user-secrets.
- If the faucet runs dry, the UI shows "Faucet is empty right now" and disables
  the button; top it up with another `transfer` to the faucet address.
- `rescueTokens` lets the owner pull unclaimed CAFE back out if the faucet is
  ever retired.
