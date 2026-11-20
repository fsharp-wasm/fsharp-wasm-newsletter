# Deploying to Cloudflare and GitHub Pages

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

## GitHub Pages

The same site is published to GitHub Pages by `.github/workflows/pages.yml` — from the `pages` artifact of a CI run, which is the
`site` package with two changes made by `build/pages.mjs`:

- Pages cannot send response headers and ignores `_headers`. The one that matters for a page that runs WebAssembly, the **content
  security policy**, is a `<meta http-equiv>` as the first element of `<head>` now, taken from the same line of `site/_headers`.
  The other headers (nosniff, referrer policy, permissions policy, cross-origin-opener policy, the cache policy of the module) are
  whatever Pages sends; they are not ours there.
- A project site lives under `/<repository>/`. The page uses relative paths only, and CI checks that: the whole end-to-end suite runs
  against `dist/pages` served under `/fsharp-wasm-newsletter`, including that the policy is enforced (a request to another origin is
  blocked by `connect-src`) and that the module is served as `application/wasm`.

| where | what |
|---|---|
| Settings → Pages → *Build and deployment* → Source | **GitHub Actions** (not "Deploy from a branch") |
| Settings → Environments → `github-pages` | created by GitHub; its rules must allow the `main` branch |

Then: Actions → Pages → *Run workflow*, or push to `main` (it publishes after a green CI run). The job prints the address and checks that
it answers, that the policy is in the page and that the module is served as `application/wasm`.

Cloudflare sends all headers of `site/_headers` and is the deployment with the full set; GitHub Pages is the one that needs no account
beyond the repository.
