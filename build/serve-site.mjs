// Serves dist/site the way Cloudflare does: static files, and the response headers of its _headers file.
//   node build/serve-site.mjs [port] [dir]
// `prefix` serves the site under a path (GitHub Pages serves a project site at /<repository>/): requests outside it are 404.
import http from "node:http";
import { readFile } from "node:fs/promises";
import { extname, join, normalize } from "node:path";
import { fileURLToPath } from "node:url";

const types = { ".html": "text/html; charset=utf-8", ".js": "text/javascript", ".mjs": "text/javascript", ".wasm": "application/wasm", ".css": "text/css", ".json": "application/json", ".png": "image/png", ".svg": "image/svg+xml" };

/** The rules of a `_headers` file: a path pattern (`/*`, `/*.wasm`, `/vendor/*`), then indented `Name: value` lines. */
export const parseHeaders = (text) => {
  const rules = [];
  for (const line of text.split("\n")) {
    if (/^\s*(#|$)/.test(line)) continue;
    if (/^\S/.test(line)) rules.push({ pattern: line.trim(), headers: [] });
    else { const i = line.indexOf(":"); rules.at(-1)?.headers.push([line.slice(0, i).trim(), line.slice(i + 1).trim()]); }
  }
  return rules;
};
const matches = (pattern, path) => new RegExp("^" + pattern.replace(/[.+?^${}()|[\]\\]/g, "\\$&").replace(/\*/g, ".*") + "$").test(path);

export const serveSite = (root, port = 0, prefix = "") => new Promise(async (resolve) => {
  const rules = parseHeaders(await readFile(join(root, "_headers"), "utf8").catch(() => ""));
  const server = http.createServer(async (req, res) => {
    try {
      const raw = decodeURIComponent(new URL(req.url, "http://x").pathname);
      if (prefix && raw !== prefix && !raw.startsWith(prefix + "/")) throw new Error("outside the prefix");
      const path = raw.slice(prefix.length) || "/";
      let file = normalize(join(root, path));
      if (!file.startsWith(root)) throw new Error("outside");
      if (extname(file) === "") file = join(file, "index.html");
      const body = await readFile(file);
      const headers = { "content-type": types[extname(file)] ?? "application/octet-stream" };
      for (const r of rules) if (matches(r.pattern, path === "/" ? "/index.html" : path)) for (const [k, v] of r.headers) headers[k] = v;
      res.writeHead(200, headers);
      res.end(body);
    } catch { res.writeHead(404); res.end("not found"); }
  }).listen(port, () => resolve({ server, base: `http://127.0.0.1:${server.address().port}${prefix}` }));
});

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const root = fileURLToPath(new URL(process.argv[3] ?? "../dist/site", import.meta.url));
  const { base } = await serveSite(root, Number(process.argv[2] ?? 8080));
  console.log(base + "/");
}
