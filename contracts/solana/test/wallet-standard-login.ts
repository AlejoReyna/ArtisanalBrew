import { expect } from "chai";
import { loginWithWallet } from "../../../src/ThisCafeteria.Web/wwwroot/js/solanaWalletAuth.js";

describe("Solana Wallet Standard browser adapter", () => {
  const address = "AddressLookupTab1e1111111111111111111111111";

  afterEach(() => {
    delete (globalThis as any).window;
    delete (globalThis as any).document;
    delete (globalThis as any).fetch;
  });

  it("binds the selected chain challenge, signs its exact bytes, and verifies the response", async () => {
    const requests: Array<{ url: string; body?: any; headers?: any }> = [];
    let signedMessage = "";
    const account = {
      address,
      publicKey: new Uint8Array(32),
      features: {
        "solana:signMessage": {
          signMessage: async ({ message }: any) => {
            signedMessage = new TextDecoder().decode(message);
            return [{ signature: new Uint8Array(64).fill(7) }];
          }
        }
      }
    };
    installBrowser([{ name: "Phantom", accounts: [account], features: { "standard:connect": { connect: async () => ({ accounts: [account] }) } } }]);
    (globalThis as any).fetch = async (url: string, options?: any) => {
      requests.push({ url, body: options?.body ? JSON.parse(options.body) : undefined, headers: options?.headers });
      if (url === "/api/chains") return reply({ selectedChainKey: "solana-localnet" });
      if (url.endsWith("/challenge")) return reply({ message: "bound challenge", nonce: "nonce", chainKey: "solana-localnet" });
      return reply({ success: true, address, redirectUrl: "/" });
    };

    const result = await loginWithWallet("Phantom");

    expect(result).to.deep.equal({ success: true, address, redirectUrl: "/" });
    expect(signedMessage).to.equal("bound challenge");
    expect(requests[1].body).to.include({ address, chainKey: "solana-localnet", walletName: "Phantom" });
    expect(requests[2].body).to.include({ address, message: "bound challenge", nonce: "nonce", chainKey: "solana-localnet" });
    expect(requests[2].headers["X-CSRF-TOKEN"]).to.equal("test-token");
  });

  it("fails closed when a discovered wallet cannot sign messages", async () => {
    const account = { address, publicKey: new Uint8Array(32), features: {} };
    installBrowser([{ name: "Phantom", accounts: [account], features: { "standard:connect": { connect: async () => ({ accounts: [account] }) } } }]);
    (globalThis as any).fetch = async (url: string) => url === "/api/chains"
      ? reply({ selectedChainKey: "solana-localnet" })
      : reply({ message: "challenge", nonce: "nonce", chainKey: "solana-localnet" });

    const result = await loginWithWallet("Phantom");

    expect(result.success).to.equal(false);
    expect(result.error).to.contain("message signing");
  });
});

function installBrowser(wallets: any[]) {
  (globalThis as any).window = {
    navigator: { wallets },
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
    dispatchEvent: () => true,
    setTimeout
  };
  (globalThis as any).document = { cookie: "XSRF-TOKEN=test-token" };
}

function reply(body: any) {
  return { ok: true, json: async () => body, text: async () => JSON.stringify(body) };
}
