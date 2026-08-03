/**
 * Entry module for the browser bundle consumed by
 * ThisCafeteria.Web/wwwroot/js/delegation-toolkit.iife.min.js. Re-exports what
 * smartAccountRegistration.js needs to derive a HybridDeleGator's counterfactual
 * address client-side, and what smartAccountActivation.js needs to sign scoped
 * session-key delegations and build/sign the owner's activation/revocation
 * UserOperation - against the caller-supplied DeleGatorEnvironment (this repo's
 * already-deployed modular stack addresses). Mirrors the exact same imports as
 * contracts/evm/scripts/metamask-session-key-e2e.ts and
 * src/ThisCafeteria.AgentGateway/src/agenticPayments.ts; nothing here deploys a
 * contract or broadcasts a transaction itself - it only composes the published
 * SDK's own delegation/execution/UserOperation construction.
 */
export {
    toMetaMaskSmartAccount,
    Implementation,
    createDelegation,
    createExecution,
    ExecutionMode,
    contracts
} from "@metamask/delegation-toolkit";
export { getDelegationHashOffchain } from "@metamask/delegation-toolkit/utils";
export { createPublicClient, createWalletClient, custom, toHex, encodeFunctionData, parseUnits } from "viem";
