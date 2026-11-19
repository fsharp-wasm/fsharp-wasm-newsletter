/// Colour checks on `#rgb` / `#rrggbb` strings: a thin layer over `FSharp.Wasm.Ui.Color`, which holds the maths (parsing, WCAG luminance and
/// contrast, oklch/hsl conversion).
module Newsletter.Color

open FSharp.Wasm.Ui

/// (r, g, b) of `#rgb` or `#rrggbb`, 0..255.
let parse (s: string) : (int * int * int) option = Color.parseHex s

let isColor (s: string) : bool = Color.isHex s

/// Contrast ratio 1..21 of two colours; None when either is not a hex colour.
let contrast (a: string) (b: string) : float option = Color.contrast (Hex a) (Hex b)

/// 4.5 : 1 is WCAG AA for normal text, 3 : 1 for large text and UI components.
let meetsAA (a: string) (b: string) : bool = Color.meetsAA (Hex a) (Hex b)
