# BSC Testnet marketplace checkout follow-up

Status: **blocked; `deployments/bsc-testnet.json` must keep
`marketplacePayment: false`**.

## Investigation result

The missing capability is not an incomplete port of the settlement rail from
`1a575e8`. That commit made the native-transfer checkout and
`EvmMarketplacePaymentGateway` chain-aware. BSC Testnet already has:

- an enabled chain manifest and wallet switch metadata;
- a configured `legacyPool` settlement destination;
- server-side verification resolved through the selected chain's trusted RPC;
- receipt status, from/to/value, confirmation-depth, and transaction-hash
  checks;
- chain/network/explorer metadata on the recorded order.

The unsafe part is pricing and receipt semantics:

1. `Checkout.razor` uses the constant `EthUsdRate = 3750` and computes
   `_ethAmount = _orderTotalUsd / EthUsdRate`.
2. The payment path explicitly refuses any native symbol other than `ETH`.
   Removing only that guard would charge the ETH-denominated number as tBNB.
3. The UI labels every native amount as ETH (`FormatEth`, “1 ETH”, and the
   `checkoutEth.js`/`amountEth` boundary).
4. Persistence and DTOs call the value `PaymentEthAmount`; profile, order, and
   admin views render it as ETH. A BNB payment stored in that column would be
   technically ambiguous even if the transfer were valid.
5. The existing `CoinGeckoEthUsdPriceService` is ETH-only and checkout does not
   use it. There is no chain-keyed, timestamped native/USD quote attached to
   the order for server verification or receipt reproduction.

## Required implementation

- Replace the checkout constant with a server-owned native-asset quote service
  keyed by chain/asset. It must return price, source, observation time, expiry,
  and raw precision rules. Failure/staleness must fail closed.
- Rename/generalize `PaymentEthAmount` to a native-amount model and add native
  symbol/decimals plus quote provenance through the entity, migration, DTOs,
  receipt, profile, orders, and admin views. Preserve legacy Sepolia rows.
- Generalize the browser module and UI copy without changing raw wei
  conversion: both ETH and tBNB currently use 18 decimals, but the value sent
  for verification must be derived from the selected manifest and the
  server-issued quote.
- Bind the quote identifier, USD order total, native amount, chain ID,
  recipient, and expiry into the server-side order/payment verification path.
  The browser must not choose the price or settlement destination.
- Add unit tests for rounding/staleness/wrong-chain quotes and integration
  coverage for order persistence/receipt rendering on both Sepolia and BSC.
- Run a funded BSC Testnet browser checkout: wallet switch → exact tBNB
  transfer → server confirmation → durable order/ledger/receipt → profile and
  orders UI. Record the public transaction hash.

Only after that public test passes may `marketplacePayment` become `true` in
`deployments/bsc-testnet.json`. The existing Sepolia native-ETH checkout must
remain unchanged throughout the migration.
