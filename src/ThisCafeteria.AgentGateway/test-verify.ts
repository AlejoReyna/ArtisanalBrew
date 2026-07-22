import { HTTPFacilitatorClient } from "@x402/core/server";
import http from "http";

const server = http.createServer((req, res) => {
  res.writeHead(200, { "Content-Type": "application/json" });
  res.end(JSON.stringify({ isValid: false }));
});

server.listen(4025, async () => {
  const client = new HTTPFacilitatorClient({ url: "http://127.0.0.1:4025" });
  try {
    const res = await client.verify({ signature: "test" } as any, {} as any);
    console.log("RES:", res);
  } catch (e) {
    console.log("ERR:", e);
  }
  server.close();
});
