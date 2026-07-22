/**
 * Builds the browser-consumable bundle of @metamask/delegation-toolkit + viem
 * used by ThisCafeteria.Web/wwwroot/js/smartAccountRegistration.js to derive a
 * HybridDeleGator's counterfactual address client-side, against this repo's
 * already-deployed modular stack (never against a re-derived/guessed one).
 *
 * Neither package ships an official browser IIFE build (unlike @solana/web3.js,
 * whose prebuilt bundle is vendored as-is under wwwroot/js/solana-web3.iife.min.js),
 * so this repo builds its own with esbuild from the pinned dependency versions
 * already used server-side by the Hardhat scripts - not a separately-chosen
 * version, and not hand-copied bytecode.
 */
import { build } from "esbuild";
import { writeFileSync, mkdirSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const repoRoot = join(here, "..", "..", "..");
const outfile = join(repoRoot, "src", "ThisCafeteria.Web", "wwwroot", "js", "delegation-toolkit.iife.min.js");
const licenseFile = join(repoRoot, "src", "ThisCafeteria.Web", "wwwroot", "js", "delegation-toolkit.LICENSE");

const delegationToolkitPkg = await import("@metamask/delegation-toolkit/package.json", { with: { type: "json" } });
const viemPkg = await import("viem/package.json", { with: { type: "json" } });

mkdirSync(dirname(outfile), { recursive: true });

await build({
  entryPoints: [join(here, "browser-delegation-bundle-entry.ts")],
  bundle: true,
  format: "iife",
  globalName: "MetaMaskDelegationToolkit",
  platform: "browser",
  target: "es2020",
  minify: true,
  outfile,
  banner: {
    js: `/* Bundled for browser use from @metamask/delegation-toolkit@${delegationToolkitPkg.default.version} ` +
      `(${delegationToolkitPkg.default.license}) and viem@${viemPkg.default.version} (${viemPkg.default.license}). ` +
      `See delegation-toolkit.LICENSE. Built by contracts/evm/scripts/build-browser-delegation-bundle.ts - do not edit by hand. */`,
  },
});

writeFileSync(
  licenseFile,
  `This bundle vendors, unmodified apart from bundling/minification:\n\n` +
    `- @metamask/delegation-toolkit@${delegationToolkitPkg.default.version} (${delegationToolkitPkg.default.license})\n` +
    `  https://www.npmjs.com/package/@metamask/delegation-toolkit\n` +
    `- viem@${viemPkg.default.version} (${viemPkg.default.license})\n` +
    `  https://www.npmjs.com/package/viem\n` +
    `\nBuilt by contracts/evm/scripts/build-browser-delegation-bundle.ts from the exact versions pinned in\n` +
    `contracts/evm/package.json. Regenerate with: npm run build:browser-delegation-bundle --prefix contracts/evm\n`,
);

console.log(`BROWSER_DELEGATION_BUNDLE_BUILT=${outfile}`);
