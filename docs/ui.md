# The Ui library: typed CSS and HTML for the web and for email

`src/Ui` (`FSharp.Wasm.Ui`) is the part of this repository that is not about newsletters. It has no dependencies, runs under .NET and as
WebAssembly GC, and is tested both ways (the Wasmecto suites of `tests/Newsletter.Tests`).

| module | what it is |
|---|---|
| `Color` | `Hex`, `Rgb`, `Hsl`, `Oklch`, `Var`; WCAG luminance and contrast; `adjustLightness` / `scaleLightness` (Sass' `color.adjust` and `color.scale`); `css Email` lowers every colour to `#rrggbb` |
| `Style` | typed values (`Px`, `Pct`, `Calc`, …), typed constructors (`Style.padding`, `Style.textAlign TextAlign.Center`, `Style.border`, …) and `Style.raw` as the escape hatch; selectors (parsed, printed, specificity); rules (`Styled`, `Media`, `Layer`, `Container`); `merge` (later wins, `!important` stays); `render Web \| Email` |
| `Html` | `Node` = element (tag, attributes, **typed style**, children) \| text \| raw \| fragment; a string renderer that escapes |
| `Inline` | applies a stylesheet to a tree: matching by tag, class, id, attribute, `:not`, descendant and child combinators; specificity then source order; the element's own style last; media queries and `:hover` stay behind for a `<style>` block |
| `Ink` | **Foundation for Emails as F#**: settings (a record), the stylesheet (a function of them), the markup (container, row, column, grid, button, callout, spacer), and `page`, which inlines and prints a whole email |

## Are we better than Sass templates?

For this job, yes, and measurably: Foundation's pipeline is Sass sources → a Sass compiler → a CSS inliner (a Node tool) → the HTML.
Here it is one F# program with no build step that runs in the browser (the live preview of the designer) and in a Worker.

* **Values have types.** `Style.padding (Px 16.0)` cannot be misspelled, `Style.textAlign TextAlign.Centre` does not compile.
* **Tokens are data, and they are checked.** The stylesheet is a function of a settings record; the contrast of every button and text colour is
  computed from it (the designer's checks do), instead of being discovered in a mail client.
* **One definition, two targets.** A colour written in `Oklch` is `oklch(…)` for the web and `#rrggbb` for an email. `Var` is a custom
  property on the web and its fallback in an email.
* **The inliner is part of the library**, so there is no second tool, and an unused rule costs nothing (nothing is inlined that nothing matches).
* **It is a library, not a template language:** loops, conditions and composition are F#.

### Fidelity to Foundation, measured

The same markup was inlined twice: by `Inline.apply` with `Ink.stylesheet`, and by [juice](https://github.com/Automattic/juice) with
Foundation's own compiled `foundation-emails.css`. Of 92 elements of a page with a grid, columns, buttons, a callout and a spacer,
**90 get identical effective inline styles**. The other two are an artefact of the comparison script (a shorthand against its longhands)
and `margin: default`, an invalid value in Foundation's compiled CSS that is not ported. Colours match to the digit
(`color.scale($primary, $lightness: 85%)` is `#def0fc`, `color.adjust(…, -20%)` is `#0f5f94`, as in Foundation's CSS).
The comparison is `tools/fidelity` (run it with a checkout of Foundation for Emails); the unit tests pin the numbers (`th.large-6` is 274 px wide at 580 px, the breakpoint is 596 px).

### What is not there yet (honestly)

* Selectors are strings that are parsed when the stylesheet is built (a mistake fails at once, and the tests build every stylesheet).
  Typed selector builders are the next step; the parser stays for literals.
* Foundation's menu, thumbnail, block grid, visibility helpers, `hr` and the `valign` rules are not ported; the button is Foundation's table
  button (no VML, so Outlook shows square corners).
* No scoped or atomic class extraction and no hashed class names for the web side; no emission tests for `@container` and `@layer` yet (the types exist).
* The email is not tested in real mail clients.

## Where this is going

```text
Color · Style   typed values, selectors, rules                      ← done (this repository)
Html            a document tree that is data                         ← done
Inline          the inliner                                          ← done
Ink             Foundation for Emails                                ← done (grid, button, callout, spacer, alignment, typography)
Web kit         scoped and atomic CSS, container queries, tokens → custom properties, a component set for pages
Scene bridge    FSharp.Graphics.Scene → Html + Style: a Semantic node becomes an element, a Stack a table or a flex box
Widgets         one widget vocabulary, three backends: the canvas (today), the DOM, email
SaaS            a Worker that renders templates (Ui + the newsletter core are pure, so the same code runs there), accounts, sending
```

The scene graph is the common intermediate: it already carries layout (`Stack`, `Padding`, `Text`, `Image`, `Clip`) and, since the
accessibility work, meaning (`Semantic`: role, name, state). A widget library for the web or for email is then a *lowering* of that graph,
not a second library: the email subset is exactly the blocks of this designer (stack, padding, text, image, button, divider, spacer, columns).
The editor kit (undo/redo with typed steps, selection, block operations, field schemas, JSON) that the widget editor of `fsharp-wasm-ide`
and this designer should share is the other half of that plan.
