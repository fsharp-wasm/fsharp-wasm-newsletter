# Changelog

## 0.1.0 — MVP

* Blocks: heading, text, image, button, quote, divider, spacer, two columns, footer; `**bold**`, `_italic_`, `[links](…)` in text.
* Outputs: HTML email (tables, inline styles, Outlook conditionals, VML buttons, preheader), web version (framework class names and
  stylesheet), plain text, project JSON.
* Framework profiles: Bootstrap 5.3 (default), Tailwind CSS 3, Custom; tokens, class map and stylesheet/script editable; profiles
  import and export as JSON.
* Checks: subject, preheader, language, alternative text, contrast (WCAG 1.4.3), heading order, link labels, footer and unsubscribe,
  Gmail's clipping limit.
* Editing: structure list, click-to-select in the preview, move/duplicate/delete, undo/redo, phone width, autosave in the browser.
* 58 checks under .NET and as WebAssembly GC; the designer driven in Chromium with axe-core.

## 0.2.0 — the email on Foundation for Emails; the Ui library; Cloudflare

* **FSharp.Wasm.Ui** (`src/Ui`): typed CSS (values, selectors, rules, rendering, merging), colour (hex, hsl, oklch, WCAG contrast, Sass-style
  adjust and scale), a pure document tree, an inliner, and the grid, button, callout, spacer, alignment and typography of Foundation for Emails
  (MIT) as F#. Against Foundation's own compiled CSS inlined by juice: 90 of 92 elements have identical effective styles (`tools/fidelity`).
* The email renderer is built on it: Foundation's table grid that stacks below its breakpoint, layout tables marked `role="presentation"`,
  bold headings, no hyphenation. (Outlook's VML button is gone: Foundation's table button shows square corners in Outlook.)
* Deployable on Cloudflare as a static site under a strict content security policy (`docs/deploy.md`).
* 77 checks under .NET and as WebAssembly GC (from 58). The compiler fixes this needed are in `fsharp-wasm` (float `%`, `String.EndsWith`).
