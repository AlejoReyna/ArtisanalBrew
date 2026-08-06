import { createHash } from "node:crypto";
import { Pool, type PoolClient } from "pg";

export const canonicalize = (value: unknown): string => {
  if (value === null || typeof value !== "object") return JSON.stringify(value);
  if (Array.isArray(value)) return `[${value.map(canonicalize).join(",")}]`;
  return `{${Object.entries(value as Record<string, unknown>).sort(([a], [b]) => a.localeCompare(b)).map(([key, item]) => `${JSON.stringify(key)}:${canonicalize(item)}`).join(",")}}`;
};

export const requestHash = (method: string, route: string, body: unknown, payment: any): string => {
  // Bind fulfillment to all security-relevant payment and request fields
  return createHash("sha256").update(canonicalize({ method: method.toUpperCase(), route, body, payment })).digest("hex");
}

export interface AtomicStore<T extends object> {
  executeAtomic(key: string, fn: () => Promise<T>): Promise<T & { replay?: boolean }>;
}

/**
 * Transient, in-memory store for idempotency caching.
 * NOT suitable for durable production-grade idempotency across multiple replicas or restarts.
 * Persistence must be implemented (e.g. Redis, database) before relying on this in production.
 */
export class IdempotencyStore<T extends object> implements AtomicStore<T> {
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

/**
 * PostgreSQL-backed atomic result store. A transaction-scoped advisory lock
 * serializes a key across every gateway replica, and the completed JSON result
 * survives restarts. The callback intentionally runs while the lock is held:
 * settlement/redemption is the critical section this store exists to protect.
 */
export class PostgresIdempotencyStore<T extends object> implements AtomicStore<T> {
  private constructor(
    private readonly pool: Pool,
    private readonly ttlMs: number,
    private readonly namespace: string,
  ) {}

  static async create<T extends object>(
    connectionString: string,
    ttlMs: number,
    namespace: string,
  ): Promise<PostgresIdempotencyStore<T>> {
    if (!connectionString) throw new Error("A PostgreSQL connection string is required");
    if (!/^[a-z0-9][a-z0-9_-]{0,62}$/i.test(namespace)) {
      throw new Error("Invalid idempotency namespace");
    }
    const pool = new Pool({
      connectionString,
      max: Number(process.env.AGENT_GATEWAY_DATABASE_POOL_SIZE ?? 10),
      statement_timeout: 15_000,
      application_name: "artisanalbrew-agent-gateway",
    });
    const store = new PostgresIdempotencyStore<T>(pool, ttlMs, namespace);
    await store.initialize();
    return store;
  }

  private async initialize(): Promise<void> {
    await this.pool.query(`
      CREATE TABLE IF NOT EXISTS agent_gateway_atomic_results (
        namespace varchar(63) NOT NULL,
        idempotency_key varchar(256) NOT NULL,
        result_json jsonb NOT NULL,
        created_at timestamptz NOT NULL DEFAULT now(),
        expires_at timestamptz NOT NULL,
        PRIMARY KEY (namespace, idempotency_key)
      )
    `);
    await this.pool.query(`
      CREATE INDEX IF NOT EXISTS ix_agent_gateway_atomic_results_expiry
      ON agent_gateway_atomic_results (expires_at)
    `);
  }

  async executeAtomic(key: string, fn: () => Promise<T>): Promise<T & { replay?: boolean }> {
    if (!key || key.length > 256) throw new Error("Invalid idempotency key");
    const client = await this.pool.connect();
    try {
      await client.query("BEGIN");
      await this.acquireLock(client, key);
      const existing = await client.query<{ result_json: T }>(
        `SELECT result_json
         FROM agent_gateway_atomic_results
         WHERE namespace = $1 AND idempotency_key = $2 AND expires_at > now()`,
        [this.namespace, key],
      );
      if (existing.rowCount) {
        await client.query("COMMIT");
        return { ...existing.rows[0].result_json, replay: true };
      }

      const value = await fn();
      await client.query(
        `INSERT INTO agent_gateway_atomic_results
           (namespace, idempotency_key, result_json, expires_at)
         VALUES ($1, $2, $3::jsonb, now() + ($4 * interval '1 millisecond'))
         ON CONFLICT (namespace, idempotency_key) DO UPDATE
         SET result_json = EXCLUDED.result_json,
             created_at = now(),
             expires_at = EXCLUDED.expires_at`,
        [this.namespace, key, JSON.stringify(value), this.ttlMs],
      );
      await client.query("COMMIT");
      return value;
    } catch (error) {
      await client.query("ROLLBACK").catch(() => undefined);
      throw error;
    } finally {
      client.release();
    }
  }

  async close(): Promise<void> {
    await this.pool.end();
  }

  private async acquireLock(client: PoolClient, key: string): Promise<void> {
    await client.query(
      "SELECT pg_advisory_xact_lock(hashtextextended($1, 0))",
      [`${this.namespace}:${key}`],
    );
  }
}
