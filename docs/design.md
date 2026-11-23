# Design notes

## What "use our IDE" meant here

The organisation's IDE ([fsharp-wasm-ide](https://github.com/fsharp-wasm/fsharp-wasm-ide)) is a set of tools written in F# and
compiled by fsharp-wasm; its first tool, the widget editor, draws one `<canvas>`. This designer follows the same pattern — a library
under test, a browser module compiled by the toolchain, a page, an end-to-end test — and is built with the same toolchain
(compiler, `lib/runtime`, `lib/fsharp-wasm-core`, Wasmecto). It is **not** drawn on a canvas: a canvas cannot take a CSS framework,
and a screen reader finds one unnamed picture in it (see the accessibility baseline in `fsharp-wasm-graphics/docs/accessibility.md`).
The designer is a DOM application on the runtime's Elmish/HTML layer, so Bootstrap's classes, the browser's own form controls and
the accessibility tree work as they are. If the intent was to host it inside the IDE's page as a second tool, that is a small
step (mount it next to the editor); it needs a decision about where the IDE gets its DOM chrome.

## The model

One record per block with a `Kind`, and a table (`fieldsOf`) that says which fields a kind uses and how to edit them. The inspector,
the JSON codec, the undo log and the checks read that table; adding a kind is one table entry, one case in each renderer, and one
line in `Lint`. The cost is a record that has fields a kind does not use; `blank`, `setField` and the codec keep them consistent.

`Studio.update` is a pure function `Msg -> Model -> Model * Effect list`. Effects (save, copy) are data; `NewsletterDesigner` turns them into
commands. That is why undo, import, selection and the announcements are tested without a DOM, under .NET and as WasmGC.

## Output

* **Email**: Foundation for Emails' structure (`FSharp.Wasm.Ui.Ink`): nested `role="presentation"` tables, a fixed-width container that
  shrinks below its breakpoint (width + gutter), the 12-column grid whose columns stack, every style inlined from a stylesheet that is a
  function of the tokens, Foundation's table button (a bordered cell around a link, no VML: Outlook shows square corners), a hidden
  preheader padded so that the first body words do not leak into the inbox preview. Two things differ from Foundation on purpose: layout
  tables say `role="presentation"`, and headings are bold and text does not hyphenate (the newsletter's own rules on top).
* **Web**: semantic HTML (`main`, `h1…h3`, `blockquote`, `footer`), the profile's class names, the stylesheet linked.
* **Text**: headings underlined, links as `label (address)`, the unsubscribe address spelled out.
* Text from the author is escaped; links go through `safeUrl` (http, https, mailto, tel, relative, merge tags — everything else
  becomes `#`); an imported profile cannot inject through a token (colours must be hex, the font stack is filtered, numbers are
  clamped). The preview frame is sandboxed (`allow-scripts` only, no `allow-same-origin`): it cannot reach the designer's page, and
  it tells the page only which block was clicked.

## Subset notes (what the compiler taught us)

The library compiles on the subset fsharp-wasm supports and runs unchanged under .NET. Things the code avoids on purpose: exceptions
with payload (the JSON parser records its first error instead), `String(char[])`, `Seq` functions over strings (index loops), list
expressions with implicit yields in hot paths (lists of options and `List.choose`). Four compiler faults were found and fixed
while building this (structural equality of unions with list fields, equality of lists of class instances such as exceptions,
`List.contains` on strings, `Math.Pow` with fractional or negative exponents); the compiler's STATUS.md lists them and the gaps
that are still open, and this repository's CI builds with the compiler revision that has the fixes (`COMPILER_REF`).

## Not in the MVP

* Drag and drop: the blocks of the structure list move by their handle, and a new block is dragged from the palette to where it should go (Escape cancels, one undo step
  each; a click on a palette button still adds one, and the click that follows a drag is swallowed). The Move buttons and the
  palette buttons stay as the keyboard way. A drag near the top or the bottom of the window scrolls the page, faster the closer the pointer is (`Interact.Scroll.near`, up to 18 px per
  frame of 60 Hz, scaled by the time between frames so that a faster screen does not scroll faster) and for as long as it rests there: `update` asks for a frame (`NextFrame`),
  the host answers with `Frame` (window height, places measured after the scroll, time), and `update` answers `ScrollBy` and the next frame until the pointer leaves the margin or the drag ends.
* Editing in place: a click on the block that is selected opens a text area over it, in the look of the block, with the text as it is written (`**bold**` and all) — for a heading, a
  paragraph, a quote, a button and a footer, their `Text`. The preview is not rendered again while it is open (the frame would take the editor away); Escape or leaving it closes it, and the typing
  is one step of the undo log. A double click is not possible: the first click selects, which renders the preview again. It is a pointer convenience; the same field is in the panel for the
  keyboard. Not edited in place: the second text of a quote and of two columns (a column is not a block), images, dividers and spacers.
* Projects live in the browser only (local storage of this origin): no account, no sync between devices or browsers, no shared projects; export the JSON to move one. See [projects.md](projects.md).
* No image upload or asset library: images are addresses (hosted by you); an email client cannot take a local file anyway.
* No per-block colours or padding: the profile's tokens apply to the whole issue. Overriding them per block is the next feature.
* No sending, no list management, no ESP integration: the export is the product. Merge tags (`{{unsubscribe_url}}`) pass through.
* The email is **not tested in real mail clients**. It follows the usual rules (tables, inline styles, a stylesheet that stays for the small-screen rules) and
  the checks cover the accessibility basics, but a pass through Litmus, Email on Acid or your own inboxes is still needed before a send.
* Dark mode of mail clients is not handled (`color-scheme` is not declared).
* The Tailwind profile uses the Play CDN (development use); the class names are Tailwind 3's.
