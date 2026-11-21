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

## 0.3.0 — on the standard libraries of fsharp-wasm-core

* JSON and the typed CSS/HTML library moved to `fsharp-wasm-core` (`FSharp.Wasm.Data`, `FSharp.Wasm.Ui`) and are used from there: the
  newsletter no longer carries a JSON parser or the Ui sources. The JSON is the union of what the backend and the newsletter each had: a
  nesting limit and strict string rules (the backend's), the position of the first error and a pretty printer (the newsletter's).
* 53 checks here, 29 in core for the libraries (82 together, from 77), under .NET and as WebAssembly GC.

## Unreleased — dragging blocks, icons, editing in place

* Editing in place: a click on the block that is selected in the preview opens a text area over it, in the look of the block, with the text as it is written; typing changes the block at once and is one undo step;
  Escape or leaving it closes it. The preview frame is not rendered again while it is open. The frame's messages (a click, the typing, its scroll position, that it has loaded) are parsed in F# (`Studio`:
  `FromPreview`, tested under .NET and as WebAssembly GC); the page no longer relays them (`Browser.Events.onFrameMessage` and `postToFrame` of the runtime). 65 checks (from 61).
* Icons: the glyphs of the buttons (↑ ↓ ⧉ ✕ ⠿) are drawn icons now, and the palette, the structure, the toggles, undo and redo, copy, download, import and the severity of a check have one. `FSharp.Wasm.Icons` (fsharp-wasm-core: typed shapes, 51 icons of Lucide) and SVG elements in the DOM reconciler of the runtime; the designer builds them as elements of the view, so a render keeps them. Decorative (`aria-hidden`), in the colour of the text; the names of the controls did not change except the palette buttons, which are "Add Heading" now. The designer module is 404 867 bytes (from 379 155: the shapes of the set).
* A new block is dragged from the palette into the list (the ghost says `New quote`, a line shows where it goes); an empty list takes it as the first block. A click on a palette
  button still adds one, and the click that the browser sends after a drag, also after Escape, is swallowed. `Ops.insertAt` of `FSharp.Wasm.Edit` turns the place into the insert.
* A block of the structure list is dragged by its handle: a press captures the pointer, a line shows where it would drop, a ghost with its name follows the
  pointer, Escape cancels, and the move is one undo step. Where it drops is decided by `FSharp.Graphics.Interact` (`fsharp-wasm-graphics`: the slot under the
  pointer, before / after / into by where in the slot it is); the edit itself is `Ops.drop` of `FSharp.Wasm.Edit` (`fsharp-wasm-core`).
* The history of the designer is `FSharp.Wasm.Edit.History` (with the coalescing of typing into one step) instead of its own past and future lists.
* The focus ring of the flat buttons (a Bootstrap button without a variant has none).
* 61 checks under .NET and as WebAssembly GC (from 53); the designer driven in Chromium drags a block and a new block with a real pointer, cancels with Escape and undoes.
