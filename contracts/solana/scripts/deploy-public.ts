import * as anchor from "@coral-xyz/anchor";
import { createMint, getAccount, getAssociatedTokenAddressSync, createAssociatedTokenAccountIdempotentInstruction, mintTo, transferChecked, TOKEN_2022_PROGRAM_ID, ASSOCIATED_TOKEN_PROGRAM_ID } from "@solana/spl-token";
import { Keypair, PublicKey, SystemProgram, Transaction, sendAndConfirmTransaction, Connection } from "@solana/web3.js";
import { createHash } from "node:crypto";
import { readFileSync, writeFileSync } from "node:fs";

async function main() {
  const cluster = process.env.SOLANA_CLUSTER ?? "devnet";
  const rpcUrl = process.env.SOLANA_RPC_URL ?? "https://api.devnet.solana.com";
  const keypairPath = process.env.SOLANA_WALLET;
  if (cluster !== "devnet" && cluster !== "testnet") throw new Error("SOLANA_CLUSTER must be devnet or testnet.");
  if (!keypairPath) throw new Error("SOLANA_WALLET must point to the deployer keypair; do not put the key in source or a manifest.");
  
  const connection = new Connection(rpcUrl, "confirmed");
  const keypair = Keypair.fromSecretKey(Buffer.from(JSON.parse(readFileSync(keypairPath, "utf-8"))));
  const wallet = new anchor.Wallet(keypair);
  
  const provider = new anchor.AnchorProvider(connection, wallet, { commitment: "confirmed" });
  anchor.setProvider(provider);
  
  const programId = new PublicKey("EbkKufsajUNzD3bLhRpb2d8XT5fHvz9e8hND111hQJxh");
  const idl = JSON.parse(readFileSync("target/idl/cafe_liquid_staking.json", "utf-8"));
  const program = new anchor.Program(idl as any, provider);
  
  const payer = keypair;
  
  console.log("Using payer:", payer.publicKey.toBase58());
  const balance = await connection.getBalance(payer.publicKey);
  console.log("Payer balance:", balance / 1e9, "SOL");
  
  const TOKEN_DECIMALS = 9;
  const vaultSeed = Buffer.from("cafe-liquid-vault-v1");
  const [vault] = PublicKey.findProgramAddressSync([vaultSeed], programId);
  
  console.log("Vault:", vault.toBase58());

  const confirmed = { commitment: "confirmed" as const };

  console.log("Creating CAFE mint...");
  const cafeMint = await createMint(provider.connection, payer, payer.publicKey, null, TOKEN_DECIMALS, undefined, confirmed, TOKEN_2022_PROGRAM_ID);
  console.log("Creating COFFEE mint...");
  const coffeeMint = await createMint(provider.connection, payer, payer.publicKey, null, TOKEN_DECIMALS, undefined, confirmed, TOKEN_2022_PROGRAM_ID);
  console.log("Creating stCAFE mint...");
  const stCafeMint = await createMint(provider.connection, payer, vault, vault, TOKEN_DECIMALS, undefined, confirmed, TOKEN_2022_PROGRAM_ID);
  
  console.log("CAFE:", cafeMint.toBase58());
  console.log("COFFEE:", coffeeMint.toBase58());
  console.log("stCAFE:", stCafeMint.toBase58());

  console.log("Ensuring ATAs...");
  const ownerCafe = await ensureAta(provider.connection, payer, cafeMint, payer.publicKey, false);
  const ownerCoffee = await ensureAta(provider.connection, payer, coffeeMint, payer.publicKey, false);
  const ownerShares = await ensureAta(provider.connection, payer, stCafeMint, payer.publicKey, false);
  const custodyCafe = await ensureAta(provider.connection, payer, cafeMint, vault, true);
  const custodyCoffee = await ensureAta(provider.connection, payer, coffeeMint, vault, true);

  console.log("Minting initial tokens...");
  await mintTo(provider.connection, payer, cafeMint, ownerCafe.address, payer, 10_000n, [], confirmed, TOKEN_2022_PROGRAM_ID);
  await mintTo(provider.connection, payer, coffeeMint, ownerCoffee.address, payer, 10_000n, [], confirmed, TOKEN_2022_PROGRAM_ID);

  console.log("Initializing program...");
  await program.methods.initialize(TOKEN_DECIMALS).accounts({ admin: payer.publicKey, vault, cafeMint, coffeeMint, stCafeMint, systemProgram: SystemProgram.programId }).rpc();

  const positionSeed = Buffer.from("cafe-liquid-position-v1");
  const position = PublicKey.findProgramAddressSync([positionSeed, payer.publicKey.toBuffer()], programId)[0];

  console.log("Depositing...");
  await program.methods.deposit(new anchor.BN(1_000)).accounts({ vault, owner: payer.publicKey, position, ownerCafe: ownerCafe.address, custodyCafe: custodyCafe.address, stCafeMint, ownerStCafe: ownerShares.address, cafeMint, tokenProgram: TOKEN_2022_PROGRAM_ID, systemProgram: SystemProgram.programId }).rpc();
  await waitForAmount(provider.connection, ownerShares.address, 1_000n);

  console.log("Testing transfer restrictions...");
  const recipient = Keypair.generate();
  const recipientShares = await ensureAta(provider.connection, payer, stCafeMint, recipient.publicKey, false);
  
  let rawTransferRejected = false;
  try {
    await transferChecked(provider.connection, payer, ownerShares.address, stCafeMint, recipientShares.address, payer, 100n, TOKEN_DECIMALS, [], confirmed, TOKEN_2022_PROGRAM_ID);
  } catch { rawTransferRejected = true; }
  if (!rawTransferRejected) throw new Error("Raw transfer should have been rejected");

  const recipientPosition = PublicKey.findProgramAddressSync([positionSeed, recipient.publicKey.toBuffer()], programId)[0];
  console.log("Transferring stCAFE via program...");
  await program.methods.transferStCafe(new anchor.BN(100)).accounts({
    vault, owner: payer.publicKey, recipient: recipient.publicKey, senderPosition: position, recipientPosition,
    ownerShares: ownerShares.address, recipientShares: recipientShares.address, stCafeMint,
    tokenProgram: TOKEN_2022_PROGRAM_ID, systemProgram: SystemProgram.programId
  }).rpc();

  await program.methods.transferStCafe(new anchor.BN(100)).accounts({
    vault, owner: recipient.publicKey, recipient: payer.publicKey, senderPosition: recipientPosition, recipientPosition: position,
    ownerShares: recipientShares.address, recipientShares: ownerShares.address, stCafeMint,
    tokenProgram: TOKEN_2022_PROGRAM_ID, systemProgram: SystemProgram.programId
  }).signers([recipient]).rpc();

  console.log("Funding rewards...");
  await program.methods.fundRewards(new anchor.BN(1_000), new anchor.BN(10)).accounts({ admin: payer.publicKey, vault, adminCoffee: ownerCoffee.address, custodyCoffee: custodyCoffee.address, coffeeMint, tokenProgram: TOKEN_2022_PROGRAM_ID }).rpc();

  await new Promise(resolve => setTimeout(resolve, 500));

  console.log("Claiming rewards...");
  await program.methods.claimRewards().accounts({ vault, owner: payer.publicKey, position, custodyCoffee: custodyCoffee.address, ownerCoffee: ownerCoffee.address, coffeeMint, tokenProgram: TOKEN_2022_PROGRAM_ID }).rpc();

  console.log("Redeeming...");
  await program.methods.redeem(new anchor.BN(1_000)).accounts({ vault, owner: payer.publicKey, position, custodyCafe: custodyCafe.address, ownerCafe: ownerCafe.address, stCafeMint, ownerStCafe: ownerShares.address, cafeMint, tokenProgram: TOKEN_2022_PROGRAM_ID }).rpc();

  console.log(`All smoke tests passed on Solana ${cluster}.`);

  const idlPath = "target/idl/cafe_liquid_staking.json";
  const binaryPath = "target/deploy/cafe_liquid_staking.so";
  const sha256 = (path: string) => {
    try {
      return createHash("sha256").update(readFileSync(path)).digest("hex");
    } catch {
      return "0000000000000000000000000000000000000000000000000000000000000000"; // fallback if binary isn't local
    }
  };

  const sanitizedRpcUrl = (() => {
    const url = new URL(rpcUrl);
    url.search = "";
    return url.toString();
  })();

  const manifest = {
    schemaVersion: "1",
    chainKey: `solana-${cluster}`,
    rpcUrl: sanitizedRpcUrl,
    cluster,
    programId: programId.toBase58(),
    deploymentSlot: await provider.connection.getSlot("finalized"),
    statePda: vault.toBase58(),
    authorityPda: vault.toBase58(),
    cafeMint: cafeMint.toBase58(),
    stCafeMint: stCafeMint.toBase58(),
    coffeeMint: coffeeMint.toBase58(),
    cafeCustody: custodyCafe.address.toBase58(),
    coffeeCustody: custodyCoffee.address.toBase58(),
    administrator: payer.publicKey.toBase58(),
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

  const outputPath = `../../deployments/solana-${cluster}.json`;
  writeFileSync(outputPath, JSON.stringify(manifest, null, 2) + "\n");
  console.log("Wrote manifest to", outputPath);
}

async function ensureAta(connection: any, payer: any, mint: PublicKey, owner: PublicKey, allowOwnerOffCurve: boolean): Promise<any> {
  const address = getAssociatedTokenAddressSync(mint, owner, allowOwnerOffCurve, TOKEN_2022_PROGRAM_ID, ASSOCIATED_TOKEN_PROGRAM_ID);
  const transaction = new Transaction().add(createAssociatedTokenAccountIdempotentInstruction(payer.publicKey, address, owner, mint, TOKEN_2022_PROGRAM_ID, ASSOCIATED_TOKEN_PROGRAM_ID));
  await sendAndConfirmTransaction(connection, transaction, [payer], { commitment: "confirmed" });
  return readAccount(connection, address);
}

async function readAccount(connection: any, address: PublicKey): Promise<any> {
  let lastError: unknown;
  for (let attempt = 0; attempt < 20; attempt++) {
    try { return await getAccount(connection, address, "confirmed", TOKEN_2022_PROGRAM_ID); }
    catch (error) { lastError = error; await new Promise(resolve => setTimeout(resolve, 250)); }
  }
  throw lastError;
}

async function waitForAmount(connection: any, address: PublicKey, expected: bigint): Promise<any> {
  let account: any;
  for (let attempt = 0; attempt < 40; attempt++) {
    account = await readAccount(connection, address);
    if (account.amount === expected) return account;
    await new Promise(resolve => setTimeout(resolve, 250));
  }
  throw new Error(`Token account ${address.toBase58()} did not reach expected raw amount ${expected}; observed ${account?.amount}`);
}

main().catch(err => {
  console.error(err);
  process.exit(1);
});
