import hardhatToolboxViem from "@nomicfoundation/hardhat-toolbox-viem";
import { defineConfig } from "hardhat/config";

export default defineConfig({
  plugins: [hardhatToolboxViem],
  solidity: {
    version: "0.8.24",
    settings: {
      optimizer: { enabled: true, runs: 200 },
      viaIR: true
    }
  },
  networks: {
    hardhat: { type: "edr-simulated", chainId: 31337 },
    localhost: { type: "http", url: "http://127.0.0.1:8545", chainId: 31337 },
    bscTestnet: {
      type: "http",
      url: process.env.BSC_TESTNET_RPC_URL ?? "https://97.rpc.thirdweb.com",
      chainId: 97,
      accounts: process.env.BSC_DEPLOYER_PRIVATE_KEY ? [process.env.BSC_DEPLOYER_PRIVATE_KEY] : []
    },
    // Local, deterministic stand-ins for the Phase 5 two-node cross-chain smoke test: two
    // SEPARATE Hardhat node processes (different ports, independent state), not one node
    // simulating two chains. chainId is 31337 for both because Hardhat 3's `node --chain-id`
    // CLI flag does not currently change what an EDR-simulated node actually reports (confirmed:
    // passing --chain-id 421614 still yields eth_chainId 0x7a69/31337) — a real, minor
    // limitation, not a design choice. This doesn't undermine the smoke test: neither resolver
    // function reads block.chainid, so "cross-chain" here is enforced by running on two
    // genuinely independent node processes, not by a matching reported chain ID label.
    arbitrumLocal: { type: "http", url: "http://127.0.0.1:8546", chainId: 31337 },
    baseLocal: { type: "http", url: "http://127.0.0.1:8547", chainId: 31337 }
  }
});
