// The designer compiled to WebAssembly GC, driven in Chromium: what a person does, judged by what the page shows, what the
// accessibility tree contains and what comes out.   node tests/e2e/designer.mjs   (after `bash build/build.sh`)
import { readFileSync, mkdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { launch, serve, checker } from '../../lib/runtime/tests/e2e/lib.mjs';
import { serveSite } from '../../build/serve-site.mjs';

const root = fileURLToPath(new URL('../..', import.meta.url));
const shots = fileURLToPath(new URL('../../docs/images/', import.meta.url));
const axeSource = readFileSync(fileURLToPath(new URL('../../node_modules/axe-core/axe.min.js', import.meta.url)), 'utf8');
const { check, failures } = checker();

// E2E_SITE=dist/site: the deployed layout, served under the headers of site/_headers (a strict content security policy); default: the checkout
const siteDir = process.env.E2E_SITE ? fileURLToPath(new URL(`../../${process.env.E2E_SITE}`, import.meta.url)) : null;
const { server, base } = siteDir ? await serveSite(siteDir, 0) : await serve(root, 0);
const pageUrl = siteDir ? `${base}/index.html` : `${base}/web/index.html`;
const { browser, page, errors } = await launch();
await page.context().grantPermissions(['clipboard-read', 'clipboard-write']);
await page.emulateMedia({ reducedMotion: 'reduce' });      // no transitions: axe reads the colours a person sees at rest
// a change reaches the page a moment after the event that caused it: poll for it instead of reading once
const poll = async (read, ms = 4000) => { const end = Date.now() + ms; for (;;) { try { if (await read()) return true; } catch {} if (Date.now() > end) return false; await page.waitForTimeout(50); } };
let firstResponse = null;
const open = async () => {
  const response = await page.goto(pageUrl);
  firstResponse ??= response;
  await page.waitForSelector('header .navbar-brand', { timeout: 20000 });
};
const frame = () => page.frameLocator('iframe.preview-frame');
const outline = () => page.locator('ol[aria-label^="Blocks"] > li');
const exported = async (label) => {
  await page.getByRole('button', { name: 'Code', exact: true }).click();
  await page.getByRole('button', { name: label, exact: true }).click();
  return page.locator('#export-text').inputValue();
};

await open();

// ── the deployed layout is served under a strict content security policy and still works ──
if (siteDir) {
  const csp = (await firstResponse.headerValue('content-security-policy')) ?? '';
  check('the site is served under a content security policy', csp.includes("default-src 'self'") && csp.includes("object-src 'none'"), csp);
  check('the module is served as application/wasm with a cache policy', await page.evaluate(async () => { const r = await fetch('NewsletterDesigner.wasm'); return r.headers.get('content-type') === 'application/wasm' && /max-age/.test(r.headers.get('cache-control') ?? ''); }));
}

// ── the first screen ──
check('the designer starts and shows the sample issue', (await outline().count()) === 10, `blocks: ${await outline().count()}`);
check('the preview is the email', (await frame().locator('h1').innerText()) === 'Small software, big difference');
check('the preview frame is sandboxed from the page', (await page.locator('iframe.preview-frame').getAttribute('sandbox')) === 'allow-scripts');
check('the sample passes the checks (no errors)', /Checks: 0 errors/.test(await page.locator('details > summary', { hasText: 'Checks' }).innerText()));
mkdirSync(shots, { recursive: true });

// ── adding, selecting, editing ──
await page.getByRole('button', { name: '+ Heading' }).click();
check('adding a block adds it to the structure', (await outline().count()) === 11);
check('the new block is selected and its fields are shown', await page.getByLabel('Text', { exact: true }).isVisible());
await page.getByLabel('Text', { exact: true }).fill('Hello from the test');
check('typing shows in the preview at once', (await frame().getByText('Hello from the test').count()) === 1);
check('the field keeps focus while the page re-renders', await page.getByLabel('Text', { exact: true }).evaluate((e) => e === document.activeElement));
check('the change is announced to screen readers', (await page.locator('[role=status][aria-live=polite]').innerText()) !== '');

// a text area keeps what is typed, and gives it back on undo (the morph must not overwrite it)
await outline().nth(2).getByRole('button').first().click();
const area = page.getByLabel('Text', { exact: true });
check('a paragraph is edited in a text area', (await area.evaluate((e) => e.tagName)) === 'TEXTAREA');
await area.fill('First paragraph.\n\nSecond paragraph, with **bold** and [a link](https://example.com/a).');
check('the text area keeps the typed text (including line breaks)', (await area.inputValue()).includes('\n\nSecond'));
check('markup renders in the preview', (await frame().locator('strong', { hasText: 'bold' }).count()) === 1 && (await frame().locator('a[href="https://example.com/a"]').count()) === 1);
await page.getByRole('button', { name: 'Undo', exact: true }).click();
check('undo restores the text area to the earlier text in one step', (await area.inputValue()).startsWith('Hello,'), await area.inputValue());
await page.getByRole('button', { name: 'Redo', exact: true }).click();
check('redo brings the typed text back', (await area.inputValue()).startsWith('First paragraph.'));

await page.getByLabel('Alignment', { exact: true }).selectOption('center');
check('a select shows the value the model holds (alignment centre)', await poll(async () => (await page.getByLabel('Alignment', { exact: true }).inputValue()) === 'center'));
check('and the preview follows', await poll(async () => (await frame().locator('p.text-center').count()) >= 1));

// ── selecting from the preview, moving, deleting ──
await frame().locator('h2').first().click();
check('a click in the preview selects that block', await poll(async () => (await page.locator('ol[aria-label^="Blocks"] [aria-current=true]').innerText()).startsWith('Heading')));
check('the selected block is outlined in the preview', await poll(async () => (await frame().locator('[data-selected]').count()) === 1));
const before = await outline().count();
await page.getByRole('button', { name: /^Move Heading \d+ up$/ }).first().click();
check('a block moves up (and the move is announced)', /moved up/.test(await page.locator('footer').innerText()));
await page.getByRole('button', { name: /^Duplicate / }).first().click();
check('duplicating adds a block', (await outline().count()) === before + 1);
await page.getByRole('button', { name: /^Delete / }).first().click();
check('deleting removes it', (await outline().count()) === before);

// ── the keyboard: everything has a name and can be reached by Tab; activate with Enter and Space ──
await page.locator('#app').getByRole('button', { name: '+ Divider' }).focus();
await page.keyboard.press('Enter');
check('Enter on a palette button adds a block', (await outline().count()) === before + 1);
await page.keyboard.press('Space');
check('Space on a palette button adds a block', (await outline().count()) === before + 2);
const unnamed = await page.evaluate(() => [...document.querySelectorAll('#app button, #app input, #app select, #app textarea')]
  .filter((e) => !(e.getAttribute('aria-label') || e.labels?.length || e.textContent.trim() || e.getAttribute('title'))).length);
check('every control has an accessible name', unnamed === 0, `${unnamed} without`);

// ── toggles and the other version ──
await page.getByRole('button', { name: 'Phone', exact: true }).click();
check('Phone narrows the preview to 375 px', (await page.locator('iframe.preview-frame').evaluate((e) => Math.round(e.getBoundingClientRect().width))) === 375);
check('the toggle says it is pressed', (await page.getByRole('button', { name: 'Phone', exact: true }).getAttribute('aria-pressed')) === 'true');
await page.getByRole('button', { name: 'Web', exact: true }).click();
check('the web version uses Bootstrap classes', (await frame().locator('a.btn.btn-primary').count()) >= 1);
check('the web version has a Bootstrap stylesheet', (await frame().locator('a.btn.btn-primary').first().evaluate((e) => getComputedStyle(e).backgroundColor)) === 'rgb(13, 110, 253)');
await page.getByRole('button', { name: 'Email', exact: true }).click();

// ── the theme: pick another profile, edit a token ──
await page.getByRole('button', { name: 'Theme', exact: true }).click();
await page.getByLabel('Profile', { exact: true }).selectOption('Tailwind CSS 3');
check('the profile select keeps showing the chosen profile', await poll(async () => (await page.getByLabel('Profile', { exact: true }).inputValue()) === 'Tailwind CSS 3'));
check('picking Tailwind switches the class names', (await exported('Web HTML')).includes('bg-blue-600'));
await page.getByRole('button', { name: 'Preview', exact: true }).click();
await page.getByRole('button', { name: 'Theme', exact: true }).click();
await page.getByLabel('Primary', { exact: true }).fill('#c026d3');
check('a token edit repaints the email', (await exported('Email HTML')).includes('background: #c026d3'));
await page.getByRole('button', { name: 'Theme', exact: true }).click();
await page.getByLabel('Primary', { exact: true }).fill('not-a-colour');
check('an invalid colour is flagged, and the email keeps a valid one', (await page.getByLabel('Primary', { exact: true }).getAttribute('aria-invalid')) === 'true' && (await exported('Email HTML')).includes('background: #0d6efd'));
await page.getByRole('button', { name: 'Preview', exact: true }).click();

// ── the output ──
const html = await exported('Email HTML');
check('the email export is a table layout with Outlook conditionals', html.includes('<!--[if mso]>') && html.includes('role="presentation"'));
check('the email export carries no preview machinery', !html.includes('postMessage') && !html.includes('data-block'));
await page.getByRole('button', { name: 'Copy', exact: true }).click();
await page.waitForFunction(() => document.querySelector('footer').textContent.includes('Copied'), null, { timeout: 5000 }).catch(() => {});
check('Copy puts exactly the shown text on the clipboard', (await page.evaluate(() => navigator.clipboard.readText())) === html);
const [download] = await Promise.all([page.waitForEvent('download'), page.getByRole('button', { name: 'Download', exact: true }).click()]);
check('Download names the file', download.suggestedFilename() === 'newsletter-email.html');
check('Download holds the same text', readFileSync(await download.path(), 'utf8') === html);
const text = await exported('Plain text');
check('the plain-text alternative has the headline and the unsubscribe link', text.includes('SMALL SOFTWARE, BIG DIFFERENCE') && text.includes('Unsubscribe: '));
const json = await exported('Project (JSON)');
check('the project is JSON a person can read', json.includes('"format": "fsharp-wasm-newsletter/1"') && json.includes('"type": "heading"'));

// ── the project survives a reload, and a file can be imported ──
await page.getByRole('button', { name: 'Preview', exact: true }).click();
await page.getByRole('button', { name: 'Newsletter', exact: true }).click();
await page.getByLabel('Subject line').fill('A subject that persists');
await page.reload();
await page.waitForSelector('header .navbar-brand');
await page.getByRole('button', { name: 'Newsletter', exact: true }).click();
check('the project is kept in the browser across a reload', (await page.getByLabel('Subject line').inputValue()) === 'A subject that persists');
check('the selected profile is kept too', (await exported('Web HTML')).includes('tailwindcss'));
await page.getByRole('button', { name: 'Preview', exact: true }).click();
await page.getByRole('button', { name: 'Newsletter', exact: true }).click();
await page.getByLabel('Project or profile (JSON)').fill('{ nope');
await page.getByRole('button', { name: 'Import', exact: true }).click();
check('an invalid file is explained, not imported', (await page.locator('#import-error').innerText()).includes('Not valid JSON'));
await page.getByLabel('Project or profile (JSON)').fill(json.replace('Small software, big difference', 'Imported subject'));
await page.getByRole('button', { name: 'Import', exact: true }).click();
check('a valid project is imported', (await page.getByLabel('Subject line').inputValue()) === 'Imported subject');

// ── the page itself: axe-core (WCAG 2.0 / 2.1 / 2.2 A and AA), keyboard stops ──
await page.getByRole('button', { name: 'Load sample', exact: true }).click();
for (const [name, steps] of [['block panel', async () => { await page.getByRole('button', { name: 'Block', exact: true }).click(); await outline().nth(1).getByRole('button').first().click(); }],
                            ['newsletter panel', async () => page.getByRole('button', { name: 'Newsletter', exact: true }).click()],
                            ['theme panel', async () => page.getByRole('button', { name: 'Theme', exact: true }).click()]]) {
  await steps();
  await page.evaluate(axeSource);
  const axe = await page.evaluate(() => axe.run(document, { runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'] } }));
  check(`axe-core finds no violation (${name})`, axe.violations.length === 0, JSON.stringify(axe.violations.map((v) => ({ id: v.id, nodes: v.nodes.length, target: v.nodes[0]?.target }))));
}
const stops = new Set();
for (let i = 0; i < 80; i++) {
  await page.keyboard.press('Tab');
  stops.add(await page.evaluate(() => { const e = document.activeElement; return e && e !== document.body ? (e.id || e.getAttribute('aria-label') || e.textContent.trim().slice(0, 24)) : ''; }));
}
check('the keyboard reaches many stops (all controls)', stops.size > 25, `${stops.size}`);
const vis = await page.evaluate(() => { const e = document.activeElement; const o = getComputedStyle(e); return o.outlineStyle !== 'none' || o.boxShadow !== 'none'; });
check('the focused control shows a focus indicator', vis);

// ── screenshots (docs) ──
await page.setViewportSize({ width: 1440, height: 900 });
await page.getByRole('button', { name: 'Block', exact: true }).click();
await outline().nth(3).getByRole('button').first().click();
await page.screenshot({ path: `${shots}designer.png` });
await page.getByRole('button', { name: 'Theme', exact: true }).click();
await page.screenshot({ path: `${shots}designer-theme.png` });

check('no errors in the page console', errors.length === 0, errors.slice(0, 3).join(' | '));
await browser.close();
server.close();
console.log(failures() === 0 ? '\nall checks passed' : `\n${failures()} check(s) failed`);
process.exit(failures() === 0 ? 0 : 1);
