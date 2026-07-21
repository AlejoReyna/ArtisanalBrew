/**
 * Exports the deployed bytecode of the canonical CanonicalEntryPointSimulations contract for the
 * .NET UserOperationSimulator to use as an eth_call state override. This bytecode is never
 * deployed on any chain (the upstream contract's own constructor refuses after block 100) — it is
 * only ever substituted for the real EntryPoint's code for the duration of a single read-only call.
 *
 * The exported bytecode is a pure function of the pinned account-abstraction package version, not
 * of any particular deployment, so this only needs to be re-run when that dependency changes.
 */
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { createHash } from "node:crypto";

const artifactPath = "artifacts/contracts/AccountAbstractionCanonical.sol/CanonicalEntryPointSimulations.json";
const artifact = JSON.parse(await readFile(artifactPath, "utf8"));

const outDir = "../../src/ThisCafeteria.Infrastructure/Resources";
await mkdir(outDir, { recursive: true });

const resource = {
  sourcePackage: "@account-abstraction/contracts@0.7.0",
  contract: "CanonicalEntryPointSimulations",
  deployedBytecode: artifact.deployedBytecode as string,
  exportedAtUtc: new Date().toISOString()
};

const outPath = `${outDir}/EntryPointSimulations.generated.json`;
await writeFile(outPath, JSON.stringify(resource, null, 2) + "\n", "utf8");

console.log(`Wrote ${outPath}`);
console.log(`deployedBytecode sha256: ${createHash("sha256").update(resource.deployedBytecode).digest("hex")}`);
