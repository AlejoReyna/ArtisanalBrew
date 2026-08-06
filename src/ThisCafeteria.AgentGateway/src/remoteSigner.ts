import {
  bytesToHex,
  getAddress,
  type Account,
  type Hex,
  type SignableMessage,
} from "viem";
import { toAccount } from "viem/accounts";

type FetchLike = typeof fetch;

const jsonWithBigInts = (value: unknown) =>
  JSON.stringify(value, (_key, item) => typeof item === "bigint" ? item.toString() : item);

const normalizeMessage = (message: SignableMessage): { message?: string; raw?: Hex } => {
  if (typeof message === "string") return { message };
  return { raw: typeof message.raw === "string" ? message.raw : bytesToHex(message.raw) };
};

/**
 * Builds a viem account whose signing operations are delegated to an isolated
 * signer service backed by KMS/HSM/Vault. The gateway process never receives
 * exportable key material. Network isolation, mTLS and signer policy belong to
 * the deployment boundary; the bearer token is an additional application gate.
 */
export function createRemoteSignerAccount(options: {
  address: string;
  signerUrl: string;
  bearerToken: string;
  fetchImpl?: FetchLike;
}): Account {
  if (!options.signerUrl.startsWith("https://") && process.env.NODE_ENV === "production") {
    throw new Error("Production signer URL must use HTTPS");
  }
  if (!options.bearerToken) throw new Error("Remote signer bearer token is required");
  const address = getAddress(options.address);
  const fetchImpl = options.fetchImpl ?? fetch;

  const sign = async (operation: "signMessage" | "signTypedData", payload: unknown): Promise<Hex> => {
    const response = await fetchImpl(new URL("/v1/signatures", options.signerUrl), {
      method: "POST",
      headers: {
        authorization: `Bearer ${options.bearerToken}`,
        "content-type": "application/json",
      },
      body: jsonWithBigInts({ address, operation, payload }),
      signal: AbortSignal.timeout(10_000),
    });
    if (!response.ok) throw new Error(`Remote signer failed with HTTP ${response.status}`);
    const body = await response.json() as { signature?: unknown };
    if (typeof body.signature !== "string" || !/^0x(?:[0-9a-fA-F]{2})+$/.test(body.signature)) {
      throw new Error("Remote signer returned an invalid signature");
    }
    return body.signature as Hex;
  };

  return toAccount({
    address,
    async signMessage({ message }) {
      return sign("signMessage", normalizeMessage(message));
    },
    async signTypedData(parameters) {
      return sign("signTypedData", parameters);
    },
    async signTransaction() {
      throw new Error("The agent signer is not authorized to sign raw transactions");
    },
  });
}

