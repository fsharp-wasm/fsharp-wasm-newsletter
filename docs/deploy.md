# Deploying to Cloudflare

The designer is a **static site**: `index.html`, a few kilobytes of JavaScript glue, Bootstrap's CSS and one WebAssembly module
(about 250 KB, 78 KB gzipped). Nothing runs on the server, nothing is uploaded, there is no database. Cloudflare serves the files
(Workers static assets, `wrangler.jsonc`) with the headers of `site/_headers`: a content security policy, `nosniff`, no referrer,
`application/wasm` with a cache policy.

## What CI checks

| step | what it checks |
|---|---|
| `build/site.sh` | assembles `dist/site` (26 files) |
| `E2E_SITE=dist/site node tests/e2e/designer.mjs` | the whole end-to-end suite against that layout, served under the headers of `site/_headers` (the same file Cloudflare reads), including that the policy and the wasm content type are sent |
| `wrangler deploy --dry-run` | the configuration is valid and the assets are found (no account) |
| artifact `site` | the package that was tested; `deploy.yml` downloads it, it does **not** rebuild |

## What the account needs (once)

| where | what |
|---|---|
| Settings → Environments | an environment `cloudflare` (add required reviewers there if a production deploy needs approval) |
| environment secret `CLOUDFLARE_API_TOKEN` | dash.cloudflare.com → My Profile → API Tokens → *Create Token* → template **Edit Cloudflare Workers**, limited to the account |
| environment secret `CLOUDFLARE_ACCOUNT_ID` | Workers & Pages overview, right-hand side |
| variable `CD_ENABLED` | Settings → Secrets and variables → Actions → Variables: `true` deploys after every successful CI run on `main`; without it only *Run workflow* deploys |

Then: Actions → Deploy → *Run workflow*. The job prints the `…workers.dev` address, checks that it answers, that the module is served as
`application/wasm` and that the policy header is there. A custom domain is set in the Cloudflare dashboard (Workers → the worker → Settings → Domains).

## Run it locally the way production serves it

```bash
bash build/build.sh && bash build/site.sh
node build/serve-site.mjs 8081        # http://127.0.0.1:8081/ under the headers of site/_headers
npx wrangler@4.162.0 dev              # or: Cloudflare's own local runtime
```
