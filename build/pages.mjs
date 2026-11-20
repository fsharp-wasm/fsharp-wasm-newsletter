// Turns the site of build/site.sh into what GitHub Pages serves.
//   node build/pages.mjs [from=dist/site] [to=dist/pages]
//
// Pages cannot send response headers, and it ignores a `_headers` file.  The one that matters for a page that runs WebAssembly is the
// content security policy, and that one a document can carry itself: the policy of site/_headers becomes a <meta http-equiv> as the
// first element of <head> (before any script or stylesheet it governs).  What a meta policy cannot do (frame-ancestors, sandbox, report-uri)
// the policy does not use; the other headers (nosniff, referrer, permissions, COOP, cache) are Pages' defaults here, not ours.
import { cpSync, readFileSync, rmSync, writeFileSync, existsSync } from "node:fs";
import { join, resolve } from "node:path";

const [from, to] = [resolve(process.argv[2] ?? "dist/site"), resolve(process.argv[3] ?? "dist/pages")];
if (!existsSync(join(from, "index.html"))) { console.error(`no site at ${from}: run build/site.sh first`); process.exit(1); }

const headers = readFileSync(join(from, "_headers"), "utf8");
const policy = headers.split("\n").map((l) => l.trim()).find((l) => l.toLowerCase().startsWith("content-security-policy:"))?.slice("content-security-policy:".length).trim();
if (!policy) { console.error("_headers has no Content-Security-Policy"); process.exit(1); }
const unsupported = ["frame-ancestors", "sandbox", "report-uri"].filter((d) => policy.includes(d));
if (unsupported.length) { console.error(`a meta policy ignores: ${unsupported.join(", ")}`); process.exit(1); }

rmSync(to, { recursive: true, force: true });
cpSync(from, to, { recursive: true });
rmSync(join(to, "_headers"));

const page = join(to, "index.html");
const html = readFileSync(page, "utf8");
const meta = `<meta http-equiv="Content-Security-Policy" content="${policy.replaceAll("&", "&amp;").replaceAll('"', "&quot;")}">`;
if (!/<head[^>]*>/i.test(html)) { console.error("index.html has no <head>"); process.exit(1); }
writeFileSync(page, html.replace(/<head[^>]*>/i, (open) => `${open}\n  ${meta}`));
console.log(`pages: ${to} (the content security policy is a <meta> now; ${policy.split(";").length} directives)`);
