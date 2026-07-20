import { createHash } from "node:crypto";

export const canonicalize = (value: unknown): string => {
  if (value === null || typeof value !== "object") return JSON.stringify(value);
  if (Array.isArray(value)) return `[${value.map(canonicalize).join(",")}]`;
  return `{${Object.entries(value as Record<string, unknown>).sort(([a], [b]) => a.localeCompare(b)).map(([key, item]) => `${JSON.stringify(key)}:${canonicalize(item)}`).join(",")}}`;
};

export const requestHash = (method: string, route: string, body: unknown, payment: any): string => {
  // Bind fulfillment to all security-relevant payment and request fields
  return createHash("sha256").update(canonicalize({ method: method.toUpperCase(), route, body, payment })).digest("hex");
}

/**
 * Transient, in-memory store for idempotency caching.
 * NOT suitable for durable production-grade idempotency across multiple replicas or restarts.
 * Persistence must be implemented (e.g. Redis, database) before relying on this in production.
 */
export class IdempotencyStore<T extends object> {
  private readonly values = new Map<string, { value?: T; promise?: Promise<T>; expiresAt: number }>();
  constructor(private readonly ttlMs: number, private readonly maxEntries: number) {}

  async executeAtomic(key: string, fn: () => Promise<T>): Promise<T & { replay?: boolean }> {
    const stored = this.values.get(key);
    if (stored && stored.expiresAt > Date.now()) {
      if (stored.promise) return stored.promise.then(val => ({ ...val, replay: true }));
      if (stored.value !== undefined) return { ...stored.value, replay: true };
    }

    if (this.values.size >= this.maxEntries && !this.values.has(key)) {
      const oldest = this.values.keys().next().value;
      if (oldest) this.values.delete(oldest);
    }

    const promise = fn()
      .then((val) => {
        this.values.set(key, { value: val, expiresAt: Date.now() + this.ttlMs });
        return val;
      })
      .catch((err) => {
        this.values.delete(key);
        throw err;
      });

    this.values.set(key, { promise, expiresAt: Date.now() + this.ttlMs });
    return promise;
  }
}
