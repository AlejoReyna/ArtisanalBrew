import * as anchor from "@coral-xyz/anchor";
import { expect } from "chai";
import { createMint, getAccount, getAssociatedTokenAddressSync, createAssociatedTokenAccountIdempotentInstruction, mintTo, transferChecked, TOKEN_2022_PROGRAM_ID, ASSOCIATED_TOKEN_PROGRAM_ID } from "@solana/spl-token";
import { Keypair, PublicKey, SystemProgram, Transaction, sendAndConfirmTransaction } from "@solana/web3.js";
import { createHash } from "node:crypto";
import { readFileSync, writeFileSync } from "node:fs";

process.env.ANCHOR_PROVIDER_URL ??= "http://127.0.0.1:8899";
process.env.ANCHOR_WALLET ??= `${process.env.HOME}/.config/solana/id.json`;

describe("cafe liquid staking local smoke", () => {
  const TOKEN_DECIMALS = 9;
  const provider = anchor.AnchorProvider.env();
  anchor.setProvider(provider);
  const program = anchor.workspace.cafeLiquidStaking as anchor.Program;
  const payer = (provider.wallet as anchor.Wallet).payer;
  const vaultSeed = Buffer.from("cafe-liquid-vault-v1");
  const [vault] = PublicKey.findProgramAddressSync([vaultSeed], program.programId);

  it("mints, funds, claims, burns, and redeems with vault custody", async () => {
    const confirmed = { commitment: "confirmed" as const };
    const cafeMint = await createMint(provider.connection, payer, payer.publicKey, null, TOKEN_DECIMALS, undefined, confirmed, TOKEN_2022_PROGRAM_ID);
    const coffeeMint = await createMint(provider.connection, payer, payer.publicKey, null, TOKEN_DECIMALS, undefined, confirmed, TOKEN_2022_PROGRAM_ID);
    const stCafeMint = await createMint(provider.connection, payer, vault, vault, TOKEN_DECIMALS, undefined, confirmed, TOKEN_2022_PROGRAM_ID);
    const ownerCafe = await ensureAta(provider.connection, payer, cafeMint, payer.publicKey, false);
    const ownerCoffee = await ensureAta(provider.connection, payer, coffeeMint, payer.publicKey, false);
    const ownerShares = await ensureAta(provider.connection, payer, stCafeMint, payer.publicKey, false);
    const custodyCafe = await ensureAta(provider.connection, payer, cafeMint, vault, true);
    const custodyCoffee = await ensureAta(provider.connection, payer, coffeeMint, vault, true);
    await mintTo(provider.connection, payer, cafeMint, ownerCafe.address, payer, 10_000n, [], confirmed, TOKEN_2022_PROGRAM_ID);
    await mintTo(provider.connection, payer, coffeeMint, ownerCoffee.address, payer, 10_000n, [], confirmed, TOKEN_2022_PROGRAM_ID);


    await program.methods.initialize(TOKEN_DECIMALS).accounts({ admin: payer.publicKey, vault, cafeMint, coffeeMint, stCafeMint, systemProgram: SystemProgram.programId }).rpc();
    const position = PublicKey.findProgramAddressSync([Buffer.from("cafe-liquid-position-v1"), payer.publicKey.toBuffer()], program.programId)[0];
    expect((await readAccount(provider.connection, ownerCafe.address)).amount).to.equal(10_000n);
    expect((await readAccount(provider.connection, custodyCafe.address)).amount).to.equal(0n);
    await program.methods.deposit(new anchor.BN(1_000)).accounts({ vault, owner: payer.publicKey, position, ownerCafe: ownerCafe.address, custodyCafe: custodyCafe.address, stCafeMint, ownerStCafe: ownerShares.address, cafeMint, tokenProgram: TOKEN_2022_PROGRAM_ID, systemProgram: SystemProgram.programId }).rpc();
    expect((await waitForAmount(provider.connection, ownerShares.address, 1_000n)).amount).to.equal(1_000n);
    expect((await waitForAmount(provider.connection, ownerCafe.address, 9_000n)).amount).to.equal(9_000n);
    expect((await waitForAmount(provider.connection, custodyCafe.address, 1_000n)).amount).to.equal(1_000n);
    expect((await readAccount(provider.connection, ownerCafe.address)).amount + (await readAccount(provider.connection, custodyCafe.address)).amount).to.equal(10_000n);
    expect((await readAccount(provider.connection, ownerShares.address)).isFrozen).to.equal(true);

    const recipient = Keypair.generate();
    const recipientShares = await ensureAta(provider.connection, payer, stCafeMint, recipient.publicKey, false);
    let rawTransferRejected = false;
    try {
      await transferChecked(provider.connection, payer, ownerShares.address, stCafeMint, recipientShares.address, payer, 100n, TOKEN_DECIMALS, [], confirmed, TOKEN_2022_PROGRAM_ID);
    } catch { rawTransferRejected = true; }
    expect(rawTransferRejected).to.equal(true, "raw Token-2022 transfers must not bypass reward checkpoints");
    const recipientPosition = PublicKey.findProgramAddressSync([Buffer.from("cafe-liquid-position-v1"), recipient.publicKey.toBuffer()], program.programId)[0];
    await program.methods.transferStCafe(new anchor.BN(100)).accounts({
      vault, owner: payer.publicKey, recipient: recipient.publicKey, senderPosition: position, recipientPosition,
      ownerShares: ownerShares.address, recipientShares: recipientShares.address, stCafeMint,
      tokenProgram: TOKEN_2022_PROGRAM_ID, systemProgram: SystemProgram.programId
    }).rpc();
    expect((await waitForAmount(provider.connection, ownerShares.address, 900n)).isFrozen).to.equal(true);
    expect((await waitForAmount(provider.connection, recipientShares.address, 100n)).isFrozen).to.equal(true);
    await program.methods.transferStCafe(new anchor.BN(100)).accounts({
      vault, owner: recipient.publicKey, recipient: payer.publicKey, senderPosition: recipientPosition, recipientPosition: position,
      ownerShares: recipientShares.address, recipientShares: ownerShares.address, stCafeMint,
      tokenProgram: TOKEN_2022_PROGRAM_ID, systemProgram: SystemProgram.programId
    }).signers([recipient]).rpc();
    expect((await waitForAmount(provider.connection, ownerShares.address, 1_000n)).isFrozen).to.equal(true);
    expect((await waitForAmount(provider.connection, recipientShares.address, 0n)).isFrozen).to.equal(true);
    expect((await readAccount(provider.connection, ownerCoffee.address)).amount).to.equal(10_000n);
    await program.methods.fundRewards(new anchor.BN(1_000), new anchor.BN(10)).accounts({ admin: payer.publicKey, vault, adminCoffee: ownerCoffee.address, custodyCoffee: custodyCoffee.address, coffeeMint, tokenProgram: TOKEN_2022_PROGRAM_ID }).rpc();
    expect((await waitForAmount(provider.connection, custodyCoffee.address, 1_000n)).amount).to.equal(1_000n);
    expect((await waitForAmount(provider.connection, ownerCoffee.address, 9_000n)).amount).to.equal(9_000n);
    await new Promise(resolve => setTimeout(resolve, 500));
    await program.methods.claimRewards().accounts({ vault, owner: payer.publicKey, position, custodyCoffee: custodyCoffee.address, ownerCoffee: ownerCoffee.address, coffeeMint, tokenProgram: TOKEN_2022_PROGRAM_ID }).rpc();
    const claimedCoffee = (await waitForDifferentAmount(provider.connection, ownerCoffee.address, 9_000n)).amount - 9_000n;
    expect(claimedCoffee).to.be.greaterThan(0n);
    expect((await readAccount(provider.connection, ownerCoffee.address)).amount + (await readAccount(provider.connection, custodyCoffee.address)).amount).to.equal(10_000n);
    await program.methods.redeem(new anchor.BN(1_000)).accounts({ vault, owner: payer.publicKey, position, custodyCafe: custodyCafe.address, ownerCafe: ownerCafe.address, stCafeMint, ownerStCafe: ownerShares.address, cafeMint, tokenProgram: TOKEN_2022_PROGRAM_ID }).rpc();
    expect((await waitForAmount(provider.connection, ownerShares.address, 0n)).amount).to.equal(0n);
    expect((await waitForAmount(provider.connection, ownerCafe.address, 10_000n)).amount).to.equal(10_000n);
    expect((await waitForAmount(provider.connection, custodyCafe.address, 0n)).amount).to.equal(0n);

    if (process.env.SOLANA_FIXTURE_OUTPUT) {
      const idlPath = "target/idl/cafe_liquid_staking.json";
      const binaryPath = "target/deploy/cafe_liquid_staking.so";
      const sha256 = (path: string) => createHash("sha256").update(readFileSync(path)).digest("hex");
      writeFileSync(process.env.SOLANA_FIXTURE_OUTPUT, JSON.stringify({
        schemaVersion: "1",
        chainKey: "solana-localnet",
        rpcUrl: process.env.ANCHOR_PROVIDER_URL,
        cluster: "localnet",
        programId: program.programId.toBase58(),
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
      }, null, 2) + "\n");
    }
  });
});

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

async function waitForDifferentAmount(connection: any, address: PublicKey, previous: bigint): Promise<any> {
  let account: any;
  for (let attempt = 0; attempt < 40; attempt++) {
    account = await readAccount(connection, address);
    if (account.amount !== previous) return account;
    await new Promise(resolve => setTimeout(resolve, 250));
  }
  throw new Error(`Token account ${address.toBase58()} did not change from expected raw amount ${previous}`);
}
