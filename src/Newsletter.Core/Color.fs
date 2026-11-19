/// Colour maths for the checks: WCAG 2.x relative luminance and contrast ratio of `#rgb` / `#rrggbb` colours.
module Newsletter.Color

let private hexDigit (c: char) : int =
    if c >= '0' && c <= '9' then int c - int '0'
    elif c >= 'a' && c <= 'f' then int c - int 'a' + 10
    elif c >= 'A' && c <= 'F' then int c - int 'A' + 10
    else -1

/// (r, g, b) of `#rgb` or `#rrggbb`, 0..255.
let parse (s: string) : (int * int * int) option =
    let t = s.Trim()
    if t.Length = 0 || t.[0] <> '#' || (t.Length <> 4 && t.Length <> 7) then None
    else
        let digit (i: int) = hexDigit t.[i]
        let valid = [ 1 .. t.Length - 1 ] |> List.forall (fun i -> digit i >= 0)
        if not valid then None
        elif t.Length = 4 then Some(digit 1 * 17, digit 2 * 17, digit 3 * 17)
        else Some(digit 1 * 16 + digit 2, digit 3 * 16 + digit 4, digit 5 * 16 + digit 6)

let isColor (s: string) : bool = (parse s).IsSome

let private channel (v: int) : float =
    let c = float v / 255.0
    if c <= 0.03928 then c / 12.92 else System.Math.Pow((c + 0.055) / 1.055, 2.4)

let luminance (r: int, g: int, b: int) : float = 0.2126 * channel r + 0.7152 * channel g + 0.0722 * channel b

/// Contrast ratio 1..21 of two colours; None when either is not a hex colour.
let contrast (a: string) (b: string) : float option =
    match parse a, parse b with
    | Some x, Some y ->
        let la, lb = luminance x, luminance y
        Some((max la lb + 0.05) / (min la lb + 0.05))
    | _ -> None

/// 4.5 : 1 is WCAG AA for normal text, 3 : 1 for large text and UI components.
let meetsAA (a: string) (b: string) : bool = match contrast a b with Some r -> r >= 4.5 | None -> true
