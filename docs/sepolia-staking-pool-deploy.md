# Sepolia Staking Pool Deployment

This app already has the web integration for staking and unstaking. The missing piece is a deployed staking pool contract.

You already deployed:

- `CafePaymentToken` (`CAFE`): the token users stake.
- `CoffeeCoin` (`COFFEE`): the token paid as staking rewards.

Now deploy:

- `CafeStakingPool`: holds staked `CAFE`, tracks each wallet's position, and pays `COFFEE` rewards from its own reward-token balance.

Source file:

```text
contracts/CafeStakingPool.sol
```

## Contract Behavior

`CafeStakingPool` exposes the functions the Blazor app expects:

```solidity
stake(uint256 amount)
unstake(uint256 amount)
withdraw(uint256 amount)
balanceOf(address account) view returns (uint256)
earned(address account) view returns (uint256)
```

The app flow is:

1. User approves the staking pool to spend `CAFE`.
2. User calls `stake(amount)`.
3. The pool transfers `CAFE` from the user into the pool.
4. The app reads `balanceOf(user)` as active staked balance.
5. User calls `unstake(amount)` to receive staked `CAFE` back.
6. The app reads `earned(user)` as pending `COFFEE` rewards.

Rewards are not minted by the pool. The pool must be funded with `COFFEE` before users can successfully call `claimRewards()`.

## Constructor Inputs

When deploying `CafeStakingPool`, pass:

```text
initialOwner:      your wallet address
stakingToken_:     deployed CafePaymentToken address
rewardToken_:      deployed CoffeeCoin address
annualRewardBps_:  520
```

`annualRewardBps_` is basis points:

- `520` = 5.2% APR
- `1000` = 10% APR
- `10000` = 100% APR

Use `520` if you want the on-chain contract to match the app's current `StakingAprPercent: 5.2`.

## Deploy With Remix

This is the fastest path because the repo does not currently have Hardhat or Foundry configured.

1. Open [Remix](https://remix.ethereum.org/).
2. Create a new file named `CafeStakingPool.sol`.
3. Paste the contents of `contracts/CafeStakingPool.sol`.
4. In the Solidity Compiler tab:
   - Compiler: `0.8.20` or newer.
   - Enable optimization.
   - Compile `CafeStakingPool.sol`.
5. In the Deploy & Run Transactions tab:
   - Environment: `Injected Provider - MetaMask`.
   - Network in MetaMask: `Sepolia`.
   - Contract: `CafeStakingPool`.
6. Deploy with constructor arguments:

```text
initialOwner      = <your wallet address>
stakingToken_     = <CafePaymentToken address>
rewardToken_      = <CoffeeCoin address>
annualRewardBps_  = 520
```

7. Confirm the deployment in MetaMask.
8. Copy the deployed `CafeStakingPool` address.

## Fund Rewards

The pool can return staked `CAFE` without reward funding, but `claimRewards()` needs `COFFEE` in the pool.

To fund it:

1. Mint or transfer `COFFEE` to your wallet.
2. Send `COFFEE` from your wallet to the deployed `CafeStakingPool` address.

If you are using your `CoffeeCoin` owner wallet, you can also call:

```solidity
CoffeeCoin.mint(<CafeStakingPool address>, <amount in wei>)
```

Example for 1,000 whole `COFFEE`:

```text
1000000000000000000000
```

## Update App Configuration

Set these values in `src/ThisCafeteria.Web/appsettings.Development.json` or environment variables:

```json
{
  "Blockchain": {
    "Network": {
      "PaymentTokenContract": "<CafePaymentToken address>",
      "StakingPoolContract": "<CafeStakingPool address>",
      "CoffeeCoinContract": "<CoffeeCoin address>",
      "StakingAprPercent": 5.2
    }
  }
}
```

Environment variable equivalents:

```bash
export Blockchain__Network__PaymentTokenContract="<CafePaymentToken address>"
export Blockchain__Network__StakingPoolContract="<CafeStakingPool address>"
export Blockchain__Network__CoffeeCoinContract="<CoffeeCoin address>"
export Blockchain__Network__StakingAprPercent="5.2"
```

Then restart the app:

```bash
dotnet watch --project src/ThisCafeteria.Web
```

## Verify In The App

After restart:

1. Open `/staking`.
2. Connect MetaMask on Sepolia.
3. Confirm the warning about missing staking config is gone.
4. Enter a `CAFE` amount.
5. Click `APPROVE & STAKE`.
6. Confirm the approval transaction.
7. Confirm the staking transaction.
8. Refresh and verify `Staked Balance` increased.
9. Enter an unstake amount.
10. Click `UNSTAKE`.
11. Confirm the transaction and verify your `CAFE` returns.

## Verify On Etherscan

After deployment, verify the contract on Sepolia Etherscan using:

- Compiler version: the same one used in Remix.
- License: `MIT`.
- Constructor args:
  - `initialOwner`
  - `stakingToken_`
  - `rewardToken_`
  - `annualRewardBps_`

If you deployed through Remix, use Remix's Etherscan verification plugin or the Sepolia Etherscan web UI.

## Important Notes

- Do not transfer `CoffeeCoin` ownership to the staking pool with this contract. This pool pays rewards from its `COFFEE` balance; it does not mint.
- Keep your existing `CoffeeCoinOwner__PrivateKey` setup if the web app still needs to mint loyalty rewards.
- The staking pool must hold enough `COFFEE` for reward claims, or `claimRewards()` will revert.
- The app currently records stake/unstake transactions and displays pending rewards. A dedicated claim button can be added later if you want users to claim staking rewards from the UI.
