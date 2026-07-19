import { mkdir, readFile, writeFile } from "node:fs/promises";
import { createHash } from "node:crypto";

const artifacts = ["CafeLiquidStakingVault", "CafeFaucet", "TestCafeToken", "TestCoffeeToken"];
await mkdir("../../src/ThisCafeteria.Web/wwwroot/contracts", { recursive: true });
for (const name of artifacts) {
  const path = `artifacts/contracts/${name}.sol/${name}.json`;
  const artifact = JSON.parse(await readFile(path, "utf8"));
  const abi = JSON.stringify(artifact.abi);
  await writeFile(`../../src/ThisCafeteria.Web/wwwroot/contracts/${name}.abi.json`, abi + "\n", "utf8");
  console.log(`${name}: ${createHash("sha256").update(abi).digest("hex")}`);
}
