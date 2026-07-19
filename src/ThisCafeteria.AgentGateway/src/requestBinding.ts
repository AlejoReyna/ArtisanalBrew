import { createHash } from "node:crypto";

export const canonicalize = (value: unknown): string => {
  if (value === null || typeof value !== "object") return JSON.stringify(value);
  if (Array.isArray(value)) return `[${value.map(canonicalize).join(",")}]`;
  return `{${Object.entries(value as Record<string, unknown>).sort(([a], [b]) => a.localeCompare(b)).map(([key, item]) => `${JSON.stringify(key)}:${canonicalize(item)}`).join(",")}}`;
};

export const requestHash = (method: string, route: string, body: unknown): string =>
  createHash("sha256").update(canonicalize({ method: method.toUpperCase(), route, body })).digest("hex");

export class IdempotencyStore<T> {
  private readonly values = new Map<string, { value: T; expiresAt: number }>();
  constructor(private readonly ttlMs: number, private readonly maxEntries: number) {}

  get(key: string): T | undefined {
    const stored = this.values.get(key);
    if (!stored || stored.expiresAt <= Date.now()) { this.values.delete(key); return undefined; }
    return stored.value;
  }

  set(key: string, value: T): void {
    if (this.values.size >= this.maxEntries && !this.values.has(key)) {
      const oldest = this.values.keys().next().value;
      if (oldest) this.values.delete(oldest);
    }
    this.values.set(key, { value, expiresAt: Date.now() + this.ttlMs });
  }
}
