import { connectedWallet } from "./solanaWalletAuth.js";

const ASSOCIATED_TOKEN_PROGRAM = "ATokenGPvbdGVxr1b2hvZbsiqW5xWH25efTNsLJA8knL";
const SYSTEM_PROGRAM = "11111111111111111111111111111111";
let notifier;
let activeConfig;

export function initSolanaLiquidStaking(config, dotNetRef) {
    if (!globalThis.solanaWeb3) throw new Error("The pinned Solana transaction library was not loaded.");
    activeConfig = config;
    notifier = dotNetRef;
    bind(".btn-liquid-deposit", "deposit", "liquid-deposit-amount");
    bind(".btn-liquid-redeem", "redeem", "liquid-redeem-amount");
    bind(".btn-liquid-claim", "claim", null);
}

function bind(selector, operation, inputId) {
    document.querySelectorAll(selector).forEach(button => {
        if (button.dataset.solanaLiquidBound === "true") return;
        button.dataset.solanaLiquidBound = "true";
        button.addEventListener("click", async () => {
            if (button.disabled || button.dataset.pending === "true") return;
            button.dataset.pending = "true";
            button.disabled = true;
            try {
                const amount = inputId ? document.getElementById(inputId)?.value?.trim() : null;
                const rawAmount = amount ? parseTokenAmount(amount, operation === "redeem" ? activeConfig.stCafeDecimals : activeConfig.cafeDecimals) : null;
                const { wallet, account } = await connectedWallet();
                if (account.address !== activeConfig.expectedWalletAddress) throw new Error("The connected Solana wallet does not match this session.");
                const signature = await submit(wallet, account, operation, rawAmount);
                await send(operation, operation, "confirmed", signature);
                await record(`record-${operation}`, account.address, signature, amount);
                await complete(operation);
            } catch (error) {
                await send(operation, operation, "error", null, error?.message || "Solana liquid-staking transaction failed.");
            } finally {
                delete button.dataset.pending;
                button.disabled = false;
            }
        });
    });
}

async function submit(wallet, account, operation, rawAmount) {
    const web3 = globalThis.solanaWeb3;
    const owner = new web3.PublicKey(account.address);
    const program = new web3.PublicKey(activeConfig.program);
    const vault = new web3.PublicKey(activeConfig.vaultPda);
    const cafeMint = new web3.PublicKey(activeConfig.cafeMint);
    const stCafeMint = new web3.PublicKey(activeConfig.stCafeMint);
    const coffeeMint = new web3.PublicKey(activeConfig.coffeeMint);
    const tokenProgram = new web3.PublicKey(activeConfig.token2022Program);
    const ownerCafe = ata(owner, cafeMint, tokenProgram);
    const ownerShares = ata(owner, stCafeMint, tokenProgram);
    const ownerCoffee = ata(owner, coffeeMint, tokenProgram);
    const [position] = web3.PublicKey.findProgramAddressSync([new TextEncoder().encode("cafe-liquid-position-v1"), owner.toBytes()], program);
    const instructions = [];
    let method;
    let keys;
    if (operation === "deposit") {
        method = "deposit";
        instructions.push(createAtaIdempotent(owner, ownerShares, owner, stCafeMint, tokenProgram));
        keys = [
            meta(vault, false, true), meta(owner, true, true), meta(position, false, true), meta(ownerCafe, false, true),
            meta(activeConfig.cafeCustody, false, true), meta(stCafeMint, false, true), meta(ownerShares, false, true),
            meta(cafeMint), meta(tokenProgram), meta(SYSTEM_PROGRAM)
        ];
    } else if (operation === "redeem") {
        method = "redeem";
        instructions.push(createAtaIdempotent(owner, ownerCafe, owner, cafeMint, tokenProgram));
        keys = [
            meta(vault, false, true), meta(owner, true, true), meta(position, false, true), meta(activeConfig.cafeCustody, false, true),
            meta(ownerCafe, false, true), meta(stCafeMint, false, true), meta(ownerShares, false, true),
            meta(cafeMint), meta(tokenProgram), meta(SYSTEM_PROGRAM)
        ];
    } else {
        method = "claim_rewards";
        instructions.push(createAtaIdempotent(owner, ownerCoffee, owner, coffeeMint, tokenProgram));
        keys = [
            meta(vault, false, true), meta(owner, true, true), meta(position, false, true), meta(activeConfig.coffeeCustody, false, true),
            meta(ownerCoffee, false, true), meta(coffeeMint), meta(tokenProgram)
        ];
    }
    const data = rawAmount === null ? await discriminator(method) : concat(await discriminator(method), u64(rawAmount));
    instructions.push(new web3.TransactionInstruction({ programId: program, keys, data }));
    const connection = new web3.Connection(activeConfig.rpcUrl, "confirmed");
    const latest = await connection.getLatestBlockhash("confirmed");
    const transaction = new web3.Transaction({ feePayer: owner, recentBlockhash: latest.blockhash }).add(...instructions);
    const feature = wallet.features?.["solana:signAndSendTransaction"];
    if (!feature?.signAndSendTransaction) throw new Error("This wallet does not support Solana sign-and-send transactions.");
    await send(operation, operation, "pending", null, `${operationLabel(operation)} in your Solana wallet.`);
    const output = await feature.signAndSendTransaction({
        account,
        chain: `solana:${activeConfig.cluster}`,
        transaction: transaction.serialize({ requireAllSignatures: false, verifySignatures: false }),
        options: { preflightCommitment: "confirmed" }
    });
    const result = Array.isArray(output) ? output[0] : output;
    if (!result?.signature) throw new Error("The wallet returned no Solana transaction signature.");
    const signature = encodeBase58(result.signature);
    const confirmation = await connection.confirmTransaction({ signature, ...latest }, "confirmed");
    if (confirmation.value.err) throw new Error(`Solana transaction failed: ${JSON.stringify(confirmation.value.err)}`);
    return signature;
}

function ata(owner, mint, tokenProgram) {
    const web3 = globalThis.solanaWeb3;
    return web3.PublicKey.findProgramAddressSync(
        [owner.toBytes(), tokenProgram.toBytes(), mint.toBytes()],
        new web3.PublicKey(ASSOCIATED_TOKEN_PROGRAM))[0];
}

function createAtaIdempotent(payer, address, owner, mint, tokenProgram) {
    const web3 = globalThis.solanaWeb3;
    return new web3.TransactionInstruction({
        programId: new web3.PublicKey(ASSOCIATED_TOKEN_PROGRAM),
        keys: [meta(payer, true, true), meta(address, false, true), meta(owner), meta(mint), meta(SYSTEM_PROGRAM), meta(tokenProgram)],
        data: new Uint8Array([1])
    });
}

function meta(address, isSigner = false, isWritable = false) {
    const PublicKey = globalThis.solanaWeb3.PublicKey;
    return { pubkey: address instanceof PublicKey ? address : new PublicKey(address), isSigner, isWritable };
}

async function discriminator(method) {
    const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(`global:${method}`));
    return new Uint8Array(digest).slice(0, 8);
}

function parseTokenAmount(value, decimals) {
    if (!/^\d+(\.\d+)?$/.test(value)) throw new Error("Enter a positive token amount.");
    const [whole, fraction = ""] = value.split(".");
    if (fraction.length > decimals) throw new Error(`This token supports at most ${decimals} decimal places.`);
    const raw = BigInt(whole) * 10n ** BigInt(decimals) + BigInt((fraction + "0".repeat(decimals)).slice(0, decimals) || "0");
    if (raw <= 0n || raw > 18_446_744_073_709_551_615n) throw new Error("The token amount is outside the supported u64 range.");
    return raw;
}

function u64(value) {
    const bytes = new Uint8Array(8);
    for (let index = 0; index < 8; index++) { bytes[index] = Number(value & 255n); value >>= 8n; }
    return bytes;
}

function concat(left, right) {
    const result = new Uint8Array(left.length + right.length);
    result.set(left); result.set(right, left.length);
    return result;
}

function encodeBase58(bytes) {
    const alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
    const digits = [0];
    for (const value of bytes) {
        let carry = value;
        for (let index = 0; index < digits.length; index++) { carry += digits[index] * 256; digits[index] = carry % 58; carry = Math.floor(carry / 58); }
        while (carry) { digits.push(carry % 58); carry = Math.floor(carry / 58); }
    }
    let result = "";
    for (const value of bytes) { if (value !== 0) break; result += "1"; }
    for (let index = digits.length - 1; index >= 0; index--) result += alphabet[digits[index]];
    return result;
}

async function record(endpoint, walletIdentifier, transactionId, expectedAmount) {
    const response = await fetch(`/staking/api/liquid/${endpoint}`, {
        method: "POST", credentials: "same-origin", headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": csrf() },
        body: JSON.stringify({ chainKey: activeConfig.chainKey, walletIdentifier, transactionId, expectedAmount })
    });
    if (!response.ok) throw new Error(await response.text() || "Server verification failed.");
    return response.json();
}

function operationLabel(operation) { return operation === "deposit" ? "Deposit CAFE" : operation === "redeem" ? "Redeem stCAFE" : "Claim COFFEE"; }
function csrf() { return decodeURIComponent(document.cookie.match(/(?:^|; )XSRF-TOKEN=([^;]*)/)?.[1] || ""); }
async function send(flow, step, status, txHash, message) { if (notifier) await notifier.invokeMethodAsync("OnTxStatusChanged", flow, step, status, txHash, message); }
async function complete(flow) { if (notifier) await notifier.invokeMethodAsync("OnTxCompleted", flow); }
