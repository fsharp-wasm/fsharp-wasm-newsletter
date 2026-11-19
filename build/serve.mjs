// A static file server for the repository root: http://127.0.0.1:8080/web/  (the page imports the glue of lib/runtime and the
// module of dist/).   node build/serve.mjs [port]
import { serve } from "../lib/runtime/tests/e2e/lib.mjs";
import { fileURLToPath } from "node:url";

const port = Number(process.argv[2] ?? 8080);
const { server } = await serve(fileURLToPath(new URL("..", import.meta.url)), port);
console.log(`http://127.0.0.1:${server.address().port}/web/`);
