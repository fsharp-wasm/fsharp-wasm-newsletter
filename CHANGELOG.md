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
