/**
 * Negative-path proof for UserOperationSubmitter, through the real production code path - not a
 * mocked unit test (UserOperationSubmitterTests already covers this with stubs; this proves the
 * same guarantee with the actual RundlerBundlerClient wired in, the way it really runs in the Web
 * app).
 *
 * Claim: a denied sponsorship never reaches the bundler at all - SubmitAsync returns `Denied`
 * before any HTTP call is made. Proven here by pointing the bundler at an address nothing listens
 * on (127.0.0.1:1) and confirming the harness returns cleanly rather than failing with a
 * connection error - if UserOperationSubmitter's own approval check were ever skipped or
 * reordered, this would surface as a fast, loud connection-refused failure instead of silently
 * passing.
 *
 * Needs no live chain, no Hardhat node, no bundler process - the whole point is that none of those
 * get touched.
 *
 * Usage: npx tsx scripts/crossstack-bundler-submit-denied-check.ts
 */
import { execFileSync } from "node:child_process";
import { writeFileSync } from "node:fs";

const SPONSOR_PROJECT = "../../tools/ThisCafeteria.CrossStackHarness";
const UNREACHABLE_BUNDLER_URL = "http://127.0.0.1:1"; // port 1 is reserved (tcpmux); nothing binds it.

// Minimal syntactically-valid op description. Values are never used for anything real: the
// approval check in UserOperationSubmitter.SubmitAsync returns before this operation's fields (or
// the chain registry, or the bundler) are ever touched.
const opDescription = {
  sender: "0x0000000000000000000000000000000000000001",
  nonce: "0",
  initCode: "0x",
  callData: "0x12345678",
  accountGasLimits: `0x${"0".repeat(64)}`,
  preVerificationGas: "100000",
  gasFees: `0x${"0".repeat(64)}`,
  target: "0x0000000000000000000000000000000000000002",
  selector: "0x12345678",
  entryPoint: "0x0000000000000000000000000000000000000003",
  accountFactory: "0x0000000000000000000000000000000000000004",
  paymaster: "0x0000000000000000000000000000000000000005",
  denied: true
};
const opPath = "/tmp/crossstack-submit-denied-op.json";
writeFileSync(opPath, JSON.stringify(opDescription, null, 2));

console.log("--- submitting an unapproved sponsorship through the real UserOperationSubmitter ---");
console.log(`bundler URL is deliberately unreachable: ${UNREACHABLE_BUNDLER_URL}`);

const out = execFileSync("dotnet", ["run", "--project", SPONSOR_PROJECT, "--", opPath, "submit"], {
  encoding: "utf8", stdio: ["ignore", "pipe", "pipe"],
  env: { ...process.env, CROSSSTACK_BUNDLER_RPC_URL: UNREACHABLE_BUNDLER_URL }
});
const line = out.trim().split("\n").filter(l => l.startsWith("{")).pop()!;
const result = JSON.parse(line);

console.log(`status=${result.status} detail="${result.detail}"`);
if (result.status !== "Denied") {
  throw new Error(`FAIL: expected status "Denied", got "${result.status}" - either the approval check didn't fire, or something else went wrong before it could.`);
}

console.log("PASS: UserOperationSubmitter refused an unapproved sponsorship without ever");
console.log("      attempting to contact the bundler - confirmed by the process completing");
console.log("      cleanly against an address nothing listens on, rather than failing with a");
console.log("      connection error.");
console.log("CROSSSTACK_BUNDLER_SUBMIT_DENIED_RESULT=PASS");
