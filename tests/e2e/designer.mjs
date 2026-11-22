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

// E2E_SITE=dist/site: the deployed layout, served under the headers of site/_headers (a strict content security policy); default: the checkout.
// E2E_SITE=dist/pages E2E_PREFIX=/fsharp-wasm-newsletter: the GitHub Pages layout (the policy is a <meta>, there are no headers, the site lives under a path).
const siteDir = process.env.E2E_SITE ? fileURLToPath(new URL(`../../${process.env.E2E_SITE}`, import.meta.url)) : null;
const { server, base } = siteDir ? await serveSite(siteDir, 0, process.env.E2E_PREFIX ?? '') : await serve(root, 0);
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
  // the policy comes as a header (Cloudflare) or as a <meta> (GitHub Pages cannot send headers); either way it must be enforced
  const header = (await firstResponse.headerValue('content-security-policy')) ?? '';
  const meta = await page.evaluate(() => document.querySelector('meta[http-equiv="Content-Security-Policy"]')?.content ?? '');
  const csp = header || meta;
  check(`the site is served under a content security policy (${header ? 'header' : 'meta'})`, csp.includes("default-src 'self'") && csp.includes("object-src 'none'"), csp);
  check('the policy is enforced: a request to another origin is blocked by connect-src', await page.evaluate(() => new Promise((resolve) => {
    document.addEventListener('securitypolicyviolation', (e) => resolve(e.violatedDirective), { once: true });
    fetch('https://csp-probe.invalid/').catch(() => {});
    setTimeout(() => resolve('no violation'), 3000);
  })) === 'connect-src');
  check('the module is served as application/wasm', await page.evaluate(async () => (await fetch('NewsletterDesigner.wasm')).headers.get('content-type') === 'application/wasm'));
  if (header) check('the module has a cache policy', await page.evaluate(async () => /max-age/.test((await fetch('NewsletterDesigner.wasm')).headers.get('cache-control') ?? '')));
}

// ── the first screen ──
check('the designer starts and shows the sample issue', (await outline().count()) === 10, `blocks: ${await outline().count()}`);
check('the preview is the email', (await frame().locator('h1').innerText()) === 'Small software, big difference');
check('the preview frame is sandboxed from the page', (await page.locator('iframe.preview-frame').getAttribute('sandbox')) === 'allow-scripts');
check('the sample passes the checks (no errors)', /Checks: 0 errors/.test(await page.locator('details > summary', { hasText: 'Checks' }).innerText()));
mkdirSync(shots, { recursive: true });

// ── icons: SVG elements of the view, decorative, kept by a render ──
const icons = await page.evaluate(() => {
  const all = [...document.querySelectorAll('#app svg.icon')];
  const SVG = 'http://www.w3.org/2000/svg';
  return {
    count: all.length,
    namespace: all.every((e) => e.namespaceURI === SVG && [...e.children].every((c) => c.namespaceURI === SVG)),
    decorative: all.every((e) => e.getAttribute('aria-hidden') === 'true' && e.getAttribute('focusable') === 'false'),
    sized: all.every((e) => { const r = e.getBoundingClientRect(); return r.width >= 10 && r.height >= 10; }),
    stroked: all.every((e) => getComputedStyle(e).stroke !== 'none'),
    inPalette: document.querySelectorAll('button[data-add] svg.icon').length,
    inRows: document.querySelectorAll('ol[aria-label^="Blocks"] > li svg.icon').length,
  };
});
check('the interface has icons (palette, structure, toolbar)', icons.count > 60, `${icons.count}`);
check('they are SVG elements in the SVG namespace', icons.namespace);
check('they are decorative: aria-hidden and not focusable', icons.decorative);
check('they have a size and are stroked in the colour of the text', icons.sized && icons.stroked);
check('each palette button has the icon of its kind and a plus', icons.inPalette === 18, `${icons.inPalette}`);
check('each row of the structure has a kind icon, a grip and four icon buttons', icons.inRows === 10 * 6, `${icons.inRows}`);
check('the palette kinds are named for a screen reader, not by their icon', (await page.getByRole('button', { name: 'Add Quote' }).count()) === 1);
await page.evaluate(() => { window.__icon = document.querySelector('button[data-add] svg.icon'); window.__path = window.__icon.firstElementChild; });


// ── adding, selecting, editing ──
await page.getByRole('button', { name: 'Add Heading' }).click();
check('adding a block adds it to the structure', (await outline().count()) === 11);
check('the new block is selected and its fields are shown', await page.getByLabel('Text', { exact: true }).isVisible());
await page.getByLabel('Text', { exact: true }).fill('Hello from the test');
check('typing shows in the preview at once', (await frame().getByText('Hello from the test').count()) === 1);
check('the field keeps focus while the page re-renders', await page.getByLabel('Text', { exact: true }).evaluate((e) => e === document.activeElement));
check('a render keeps the icons: the same svg element and the same path are still there', await page.evaluate(() => document.querySelector('button[data-add] svg.icon') === window.__icon && window.__icon.firstElementChild === window.__path && window.__icon.isConnected));
check('the change is announced to screen readers', (await page.locator('[role=status][aria-live=polite]').innerText()) !== '');

// a text area keeps what is typed, and gives it back on undo (the morph must not overwrite it)
await outline().nth(2).locator('button[data-select]').click();
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
// ── editing the text in place: a click on the block that is selected opens an editor over it ──
const heading = frame().locator('h2').first();
const headingText = await heading.innerText();
await frame().locator('body').evaluate(() => { window.__sameDocument = 'yes'; });          // this document: a render again would make a new one
await heading.click();                                                                      // the block is selected already
const editor = frame().locator('#__edit');
check('a click on the selected block opens an editor over it, with the text as it is written', await poll(async () => (await editor.inputValue()) === headingText));
const look = await editor.evaluate((e) => { const c = getComputedStyle(e); return { weight: parseInt(c.fontWeight, 10), size: c.fontSize, family: c.fontFamily.slice(0, 30), background: c.backgroundColor }; });
check('the editor has the look of the block', look.weight >= 600 && look.background !== 'rgba(0, 0, 0, 0)', JSON.stringify(look));
await editor.fill('Edited in place');
check('what is typed in the editor changes the block at once (its panel shows it)', await poll(async () => (await page.getByLabel('Text', { exact: true }).inputValue()) === 'Edited in place'));
check('the preview is not rendered again while the editor is open', (await frame().locator('body').evaluate(() => window.__sameDocument)) === 'yes' && (await editor.count()) === 1);
check('the editor keeps the focus while the page re-renders', await editor.evaluate((e) => e === document.activeElement));
await editor.pressSequentially(' more');
check('it goes on: more typing, the same editor', await poll(async () => (await page.getByLabel('Text', { exact: true }).inputValue()) === 'Edited in place more') && (await editor.count()) === 1);
await page.keyboard.press('Escape');
check('Escape closes the editor and the preview shows the text', await poll(async () => (await editor.count()) === 0 && (await heading.innerText()) === 'Edited in place more'));
await page.getByRole('button', { name: 'Undo', exact: true }).click();
check('undo takes the whole typing back in one step', await poll(async () => (await heading.innerText()) === headingText));

const before = await outline().count();
await page.getByRole('button', { name: /^Move Heading \d+ up$/ }).first().click();
check('a block moves up (and the move is announced)', /moved up/.test(await page.locator('footer').innerText()));
await page.getByRole('button', { name: /^Duplicate / }).first().click();
check('duplicating adds a block', (await outline().count()) === before + 1);
await page.getByRole('button', { name: /^Delete / }).first().click();
check('deleting removes it', (await outline().count()) === before);

// ── dragging a block with the pointer: the handle, the ghost, the mark, the drop, undo, Escape ──
const viewport = page.viewportSize();
await page.setViewportSize({ width: 1400, height: 1600 });            // every block of the list is on screen: the pointer can reach all of them
const kinds = () => outline().evaluateAll((els) => els.map((e) => e.querySelector('button[data-select]').innerText.split('\n')[0].trim()));
const dnd = (what) => page.locator(`[data-dnd=${what}]`);
const grab = async (i) => { const b = await outline().nth(i).locator('button[aria-label^="Drag"]').boundingBox(); await page.mouse.move(b.x + b.width / 2, b.y + b.height / 2); await page.mouse.down(); };
const order0 = await kinds();
const lastBox = await outline().nth(order0.length - 1).boundingBox();
await grab(0);
check('a press that has not moved is not a drag: no ghost yet', (await dnd('ghost').count()) === 0);
await page.mouse.move(lastBox.x + 60, lastBox.y - 40, { steps: 3 });
check('once the pointer has moved, the block under it is shown as a ghost', await poll(async () => (await dnd('ghost').count()) === 1));
await page.mouse.move(lastBox.x + 60, lastBox.y + lastBox.height - 4, { steps: 6 });    // the lower half of the last block
check('a mark shows where it would drop', await poll(async () => (await dnd('mark').count()) === 1));
check('the mark takes no pointer events', await dnd('mark').evaluate((e) => getComputedStyle(e).pointerEvents === 'none'));
await page.mouse.up();
const order1 = await kinds();
check('dropping moves the first block behind the last', order1.length === order0.length && order1[order1.length - 1] === order0[0] && order1[0] === order0[1], `${order0.join(',')} -> ${order1.join(',')}`);
check('the move by dragging is announced', await poll(async () => /moved by dragging/.test(await page.locator('footer').innerText())));
check('the mark and the ghost are gone after the drop', (await dnd('mark').count()) === 0 && (await dnd('ghost').count()) === 0);
await page.getByRole('button', { name: 'Undo', exact: true }).click();
check('undo puts the dragged block back in one step', (await kinds()).join() === order0.join());
await grab(1);
await page.mouse.move(lastBox.x + 60, lastBox.y + lastBox.height - 4, { steps: 8 });
await page.keyboard.press('Escape');
check('Escape cancels the drag: nothing moved, no mark', await poll(async () => (await dnd('mark').count()) === 0 && (await dnd('ghost').count()) === 0) && (await kinds()).join() === order0.join());
await page.mouse.up();
check('releasing after Escape drops nothing', (await kinds()).join() === order0.join());
// ── dragging a new block from the palette into the list ──
const quoteButton = page.getByRole('button', { name: 'Add Quote' });
const countNow = (await kinds()).length;
const quoteBox = await quoteButton.boundingBox();
const second = await outline().nth(1).boundingBox();
await page.mouse.move(quoteBox.x + quoteBox.width / 2, quoteBox.y + quoteBox.height / 2);
await page.mouse.down();
await page.mouse.move(second.x + 60, second.y + 6, { steps: 8 });                       // the upper half of the second block
check('the ghost names the kind that is dragged', await poll(async () => /New quote/.test(await dnd('ghost').innerText())));
check('a mark shows where the new block would go', await poll(async () => (await dnd('mark').count()) === 1));
await page.mouse.up();
check('dropping adds the block there', await poll(async () => (await kinds()).length === countNow + 1 && (await kinds())[1].startsWith('Quote')), (await kinds()).join(','));
await page.waitForTimeout(300);                                                            // a click after the drop would add a second one
check('the drop adds exactly one block (the click after a drag is swallowed)', (await kinds()).length === countNow + 1);
check('the new block is selected', (await outline().nth(1).locator('button[data-select]').getAttribute('aria-current')) === 'true');
check('the drop of a new block is announced', await poll(async () => /added as .* by dragging/.test(await page.locator('footer').innerText())));
await page.getByRole('button', { name: 'Undo', exact: true }).click();
check('undo takes the dropped block out again', (await kinds()).length === countNow);
await quoteButton.click();
check('a click on the palette button still adds a block (and only one)', await poll(async () => (await kinds()).length === countNow + 1));
await page.getByRole('button', { name: 'Undo', exact: true }).click();
await page.mouse.move(quoteBox.x + quoteBox.width / 2, quoteBox.y + quoteBox.height / 2);
await page.mouse.down();
await page.mouse.move(second.x + 60, second.y + 6, { steps: 8 });
await page.keyboard.press('Escape');
await page.mouse.up();
await page.waitForTimeout(300);
check('Escape cancels a new block: nothing added, no mark', (await kinds()).length === countNow && (await dnd('mark').count()) === 0 && (await dnd('ghost').count()) === 0);
await page.setViewportSize(viewport ?? { width: 1280, height: 720 });

// ── the keyboard: everything has a name and can be reached by Tab; activate with Enter and Space ──
await page.locator('#app').getByRole('button', { name: 'Add Divider' }).focus();
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

// ── projects: a library of newsletters in the browser ──
const projectsButton = () => page.getByRole('button', { name: /^Projects \(/ });
const rows = () => page.locator('#projects li[data-project]');
const rowNames = async () => (await rows().locator('.fw-semibold').allInnerTexts()).map((t) => t.replace(' (open)', ''));
const openProjects = async () => { if ((await page.locator('#projects').count()) === 0) await projectsButton().click(); };
await page.getByRole('button', { name: 'Newsletter', exact: true }).click();
const firstSubject = await page.getByLabel('Subject line').inputValue();
await openProjects();
check('the projects button says how many there are', (await projectsButton().innerText()).includes('(1)'));
check('the library lists the project that was always there, open', (await rows().count()) === 1 && (await rows().first().getAttribute('data-project')) === 'p1' && (await rows().first().locator('[aria-current="true"]').count()) === 1);
check('a project shows its subject, its size and when it was saved', (await rows().first().innerText()).includes(firstSubject) && /\d+ blocks? · saved \d{4}-\d{2}-\d{2} \d{2}:\d{2} UTC/.test(await rows().first().innerText()));
await page.getByRole('button', { name: 'New newsletter', exact: true }).click();
check('a new newsletter is made and opened, and the list closes', await poll(async () => (await page.locator('#projects').count()) === 0 && (await page.getByLabel('Subject line').inputValue()) === ''));
check('it has no blocks', (await outline().count()) === 0);
check('it keeps the theme of the one before', (await exported('Web HTML')).includes('tailwindcss') || (await exported('Web HTML')).includes('bootstrap'));
await page.getByLabel('Name (only for you)').fill('Launch plan');
await page.getByLabel('Subject line').fill('We launch on Friday');
await openProjects();
check('the list follows what is typed: name and subject', await poll(async () => { const names = await rowNames(); return names.length === 2 && names[0] === 'Launch plan'; }), (await rowNames()).join(' | '));
check('the newest is first', (await rows().first().getAttribute('data-project')) === 'p2');
await page.getByLabel('Search', { exact: true }).fill('friday');
check('the search finds a project by its subject', await poll(async () => (await rows().count()) === 1 && (await rowNames())[0] === 'Launch plan'));
await page.getByLabel('Search', { exact: true }).fill('no such thing');
check('and says so when nothing matches', await poll(async () => (await rows().count()) === 0 && (await page.locator('#projects').innerText()).includes('No newsletter matches')));
await page.getByLabel('Search', { exact: true }).fill('');
await page.reload();
await page.waitForSelector('header .navbar-brand');
check('after a reload the library is there and the last project is open', await poll(async () => (await projectsButton().innerText()).includes('(2)')) && (await page.getByRole('button', { name: 'Newsletter', exact: true }).click(), await page.getByLabel('Name (only for you)').inputValue()) === 'Launch plan');
await openProjects();
await rows().filter({ has: page.locator('[data-project="p1"]') }).count();
await page.locator('li[data-project="p1"] button.btn-flat').click();
check('opening another project brings its document', await poll(async () => (await page.getByLabel('Subject line').inputValue()) === firstSubject), await page.getByLabel('Subject line').inputValue());
check('and the list closes', (await page.locator('#projects').count()) === 0);
await openProjects();
await page.getByRole('button', { name: /^Duplicate / }).first().click();
check('a project is duplicated: a copy beside it', await poll(async () => (await rows().count()) === 3 && (await rowNames()).some((n) => n.endsWith('(copy)'))), (await rowNames()).join(' | '));
check('the one that was open stays open', (await rows().filter({ has: page.locator('[aria-current="true"]') }).getAttribute('data-project')) === 'p1');
await page.screenshot({ path: shots + 'designer-projects.png', clip: { x: 0, y: 0, width: 1440, height: 420 } });
const rowsBefore = await rows().count();
await page.getByRole('button', { name: 'Delete Launch plan', exact: true }).click();
check('deleting asks first, and Keep keeps it', (await page.getByRole('button', { name: 'Keep', exact: true }).count()) === 1);
await page.getByRole('button', { name: 'Keep', exact: true }).click();
check('nothing was deleted', (await rows().count()) === rowsBefore);
await page.getByRole('button', { name: 'Delete Launch plan', exact: true }).click();
await page.getByRole('button', { name: 'Delete', exact: true }).click();
check('after the question the project is gone', await poll(async () => (await rows().count()) === rowsBefore - 1 && !(await rowNames()).includes('Launch plan')));
const keys = await page.evaluate(() => Object.keys(localStorage).filter((k) => k.startsWith('fsharp-wasm-newsletter/')).sort());
check('the browser keeps an index and a key for each project, and nothing of the one that was deleted', keys.includes('fsharp-wasm-newsletter/projects') && keys.filter((k) => k.startsWith('fsharp-wasm-newsletter/project/')).length === rowsBefore - 1 && !keys.includes('fsharp-wasm-newsletter/project/p2'), keys.join(' '));
check('the page of the projects has no axe violation', await (async () => { await page.evaluate(axeSource); const r = await page.evaluate(() => axe.run(document.querySelector('#projects'), { runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'] } })); return r.violations.length === 0 || (console.log(JSON.stringify(r.violations.map((v) => ({ id: v.id, n: v.nodes.map((x) => x.target) })))), false); })());
// the single project of the versions before the library: read, kept as the library's first, and its old key forgotten
const old = await page.evaluate(() => localStorage.getItem('fsharp-wasm-newsletter/project/p1'));
await page.evaluate((v) => { for (const k of Object.keys(localStorage)) if (k.startsWith('fsharp-wasm-newsletter/')) localStorage.removeItem(k); localStorage.setItem('fsharp-wasm-newsletter/project', v); }, old);
await page.reload();
await page.waitForSelector('header .navbar-brand');
check('a project kept by an earlier version becomes the first of a library', await poll(async () => (await projectsButton().innerText()).includes('(1)')));
check('and its old key is gone', await poll(async () => (await page.evaluate(() => localStorage.getItem('fsharp-wasm-newsletter/project'))) === null && (await page.evaluate(() => localStorage.getItem('fsharp-wasm-newsletter/project/p1'))) !== null));
await page.getByRole('button', { name: 'Newsletter', exact: true }).click();
check('with its document', (await page.getByLabel('Subject line').inputValue()) === firstSubject);

// ── the page itself: axe-core (WCAG 2.0 / 2.1 / 2.2 A and AA), keyboard stops ──
await page.getByRole('button', { name: 'Load sample', exact: true }).click();
for (const [name, steps] of [['block panel', async () => { await page.getByRole('button', { name: 'Block', exact: true }).click(); await outline().nth(1).locator('button[data-select]').click(); }],
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
const focus = await page.evaluate(() => { const e = document.activeElement; const o = getComputedStyle(e); return { shown: o.outlineStyle !== 'none' || o.boxShadow !== 'none', element: `${e.tagName} ${e.id || e.getAttribute('aria-label') || e.className}` }; });
check('the focused control shows a focus indicator', focus.shown, focus.element);

// ── screenshots (docs) ──
await page.setViewportSize({ width: 1440, height: 900 });
await page.getByRole('button', { name: 'Block', exact: true }).click();
await outline().nth(3).locator('button[data-select]').click();
await page.screenshot({ path: `${shots}designer.png` });
await page.getByRole('button', { name: 'Theme', exact: true }).click();
await page.screenshot({ path: `${shots}designer-theme.png` });

// (the policy probe above provokes two console errors on purpose)
const unexpected = errors.filter((e) => !e.includes('csp-probe.invalid'));
check('no errors in the page console', unexpected.length === 0, unexpected.slice(0, 3).join(' | '));
await browser.close();
server.close();
console.log(failures() === 0 ? '\nall checks passed' : `\n${failures()} check(s) failed`);
process.exit(failures() === 0 ? 0 : 1);
