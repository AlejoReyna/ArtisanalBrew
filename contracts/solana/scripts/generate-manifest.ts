import { PublicKey } from "@solana/web3.js";
import { getAssociatedTokenAddressSync, TOKEN_2022_PROGRAM_ID, ASSOCIATED_TOKEN_PROGRAM_ID } from "@solana/spl-token";
import { readFileSync, writeFileSync } from "node:fs";
import { createHash } from "node:crypto";

const programId = new PublicKey("EbkKufsajUNzD3bLhRpb2d8XT5fHvz9e8hND111hQJxh");
const vault = new PublicKey("2NyAMgREBZuYfLwiwR3LLqazR1cM3Bebsu51qosFYDGB");
const cafeMint = new PublicKey("4gw7cXQwqZ1SQnSfbY4VJy1uLZRwbMiZwJNoSchWgQM4");
const coffeeMint = new PublicKey("9r6Dd9VDv6nPuQabwuNBkvJ3kLNLrYSNwXG4ipSUkg4D");
const stCafeMint = new PublicKey("HiBd5DHXLkmbv3MMpDXm63jqRiUUTDQBuQdY8EYruAg1");
const admin = new PublicKey("9dCm3Tm8zMQvm4XWBXBjGHKZVg6Fd9rdSPEST8Zx2aDR");
const TOKEN_DECIMALS = 9;

const cafeCustody = getAssociatedTokenAddressSync(cafeMint, vault, true, TOKEN_2022_PROGRAM_ID, ASSOCIATED_TOKEN_PROGRAM_ID);
const coffeeCustody = getAssociatedTokenAddressSync(coffeeMint, vault, true, TOKEN_2022_PROGRAM_ID, ASSOCIATED_TOKEN_PROGRAM_ID);

const idlPath = "target/idl/cafe_liquid_staking.json";
const binaryPath = "target/deploy/cafe_liquid_staking.so";
const sha256 = (path: string) => {
  try {
    return createHash("sha256").update(readFileSync(path)).digest("hex");
  } catch {
    return "0000000000000000000000000000000000000000000000000000000000000000";
  }
};

const manifest = {
  schemaVersion: "1",
  chainKey: "solana-testnet",
  rpcUrl: "https://api.testnet.solana.com/",
  cluster: "testnet",
  programId: programId.toBase58(),
  deploymentSlot: 422939391, // From user prompt
  statePda: vault.toBase58(),
  authorityPda: vault.toBase58(),
  cafeMint: cafeMint.toBase58(),
  stCafeMint: stCafeMint.toBase58(),
  coffeeMint: coffeeMint.toBase58(),
  cafeCustody: cafeCustody.toBase58(),
  coffeeCustody: coffeeCustody.toBase58(),
  administrator: admin.toBase58(),
  tokenProgram: "TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA",
  token2022Program: TOKEN_2022_PROGRAM_ID.toBase58(),
  cafeDecimals: TOKEN_DECIMALS,
  stCafeDecimals: TOKEN_DECIMALS,
  coffeeDecimals: TOKEN_DECIMALS,
  idlPath,
  idlSha256: sha256(idlPath),
  programBinarySha256: sha256(binaryPath),
  capabilities: { walletLogin: true, liquidStaking: true, rewardFunding: true, reconciliation: true }
};

const outputPath = "../../deployments/solana-testnet.json";
writeFileSync(outputPath, JSON.stringify(manifest, null, 2) + "\n");
console.log("Wrote manifest to", outputPath);
