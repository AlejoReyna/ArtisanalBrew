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
    }
  }
});
