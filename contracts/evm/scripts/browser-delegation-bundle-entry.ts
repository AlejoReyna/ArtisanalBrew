/**
 * Entry module for the browser bundle consumed by
 * ThisCafeteria.Web/wwwroot/js/delegation-toolkit.iife.min.js. Re-exports only what
 * smartAccountRegistration.js needs to derive a HybridDeleGator's counterfactual
 * address client-side against the caller-supplied DeleGatorEnvironment (this
 * repo's already-deployed modular stack addresses) - it never deploys or signs
 * a delegation itself.
 */
export { toMetaMaskSmartAccount, Implementation } from "@metamask/delegation-toolkit";
export { createPublicClient, createWalletClient, custom, toHex } from "viem";
