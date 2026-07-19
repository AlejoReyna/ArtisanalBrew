import { expect } from "chai";
import { initSolanaLiquidStaking } from "../../../src/ThisCafeteria.Web/wwwroot/js/solanaLiquidStaking.js";

describe("Solana liquid-staking browser adapter", () => {
  afterEach(() => {
    for (const name of ["window", "document", "fetch", "solanaWeb3"]) delete (globalThis as any)[name];
  });

  it("builds, signs, confirms, and records a deposit through Wallet Standard", async () => {
    const address = "AddressLookupTab1e1111111111111111111111111";
    let click: (() => Promise<void>) | undefined;
    let walletInput: any;
    let recorded: any;
    const button: any = {
      dataset: {}, disabled: false,
      addEventListener: (_: string, handler: () => Promise<void>) => { click = handler; }
    };
    const account = { address, publicKey: new Uint8Array(32), features: [] };
    const wallet = {
      name: "Phantom",
      accounts: [account],
      features: {
        "standard:connect": { connect: async () => ({ accounts: [account] }) },
        "solana:signMessage": { signMessage: async () => [{ signature: new Uint8Array(64) }] },
        "solana:signAndSendTransaction": {
          signAndSendTransaction: async (input: any) => { walletInput = input; return [{ signature: new Uint8Array(64).fill(3) }]; }
        }
      }
    };
    installBrowser([wallet]);
    (globalThis as any).document = {
      cookie: "XSRF-TOKEN=token",
      querySelectorAll: (selector: string) => selector === ".btn-liquid-deposit" ? [button] : [],
      getElementById: () => ({ value: "1.25" })
    };
    (globalThis as any).fetch = async (_url: string, options: any) => {
      recorded = JSON.parse(options.body);
      return { ok: true, json: async () => ({ success: true }), text: async () => "" };
    };
    (globalThis as any).solanaWeb3 = fakeWeb3();
    const notifications: any[] = [];
    initSolanaLiquidStaking(config(address), { invokeMethodAsync: async (...args: any[]) => notifications.push(args) });

    await click!();

    expect(walletInput.chain).to.equal("solana:testnet");
    expect(walletInput.transaction).to.deep.equal(new Uint8Array([9, 9, 9]));
    expect(recorded).to.include({ chainKey: "solana-testnet", walletIdentifier: address, expectedAmount: "1.25" });
    expect(recorded.transactionId).to.be.a("string").and.not.empty;
    expect(notifications.some(items => items[0] === "OnTxCompleted" && items[1] === "deposit")).to.equal(true);
  });
});

function installBrowser(wallets: any[]) {
  (globalThis as any).window = {
    navigator: { wallets }, addEventListener: () => undefined, removeEventListener: () => undefined,
    dispatchEvent: () => true, setTimeout
  };
}

function fakeWeb3() {
  class PublicKey {
    value: string;
    constructor(value: any) { this.value = typeof value === "string" ? value : "derived"; }
    toBytes() { return new Uint8Array(32); }
    static findProgramAddressSync() { return [new PublicKey("derived"), 255]; }
  }
  class TransactionInstruction { constructor(public value: any) {} }
  class Transaction {
    instructions: any[] = [];
    constructor(public value: any) {}
    add(...items: any[]) { this.instructions.push(...items); return this; }
    serialize() { return new Uint8Array([9, 9, 9]); }
  }
  class Connection {
    async getLatestBlockhash() { return { blockhash: "hash", lastValidBlockHeight: 10 }; }
    async confirmTransaction() { return { value: { err: null } }; }
  }
  return { PublicKey, TransactionInstruction, Transaction, Connection };
}

function config(address: string) {
  return {
    chainKey: "solana-testnet", cluster: "testnet", rpcUrl: "https://api.testnet.solana.com",
    program: "program", vaultPda: "vault", cafeMint: "cafe", stCafeMint: "stcafe", coffeeMint: "coffee",
    cafeCustody: "cafe-custody", coffeeCustody: "coffee-custody", token2022Program: "token-2022",
    cafeDecimals: 9, stCafeDecimals: 9, coffeeDecimals: 9, expectedWalletAddress: address
  };
}
