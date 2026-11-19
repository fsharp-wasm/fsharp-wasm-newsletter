namespace FSharp.Wasm.Ui

open System

/// A colour as the author writes it.  Tokens can be perceptual (`Oklch`); for an email they are lowered to hex.
type Color =
    | Hex of string
    | Rgb of int * int * int
    /// hue in degrees, saturation and lightness 0..1
    | Hsl of float * float * float
    /// lightness 0..1, chroma (about 0..0.4), hue in degrees
    | Oklch of float * float * float
    | Transparent
    | Inherit
    | CurrentColor
    /// a custom property with an optional fallback (web only; an email takes the fallback)
    | Var of string * Color option

module Color =
    let private hexDigit (c: char) : int =
        if c >= '0' && c <= '9' then int c - int '0'
        elif c >= 'a' && c <= 'f' then int c - int 'a' + 10
        elif c >= 'A' && c <= 'F' then int c - int 'A' + 10
        else -1

    /// (r, g, b) of `#rgb` or `#rrggbb`, 0..255.
    let parseHex (s: string) : (int * int * int) option =
        let t = s.Trim()
        if t.Length = 0 || t.[0] <> '#' || (t.Length <> 4 && t.Length <> 7) then None
        else
            let digit (i: int) = hexDigit t.[i]
            if not ([ 1 .. t.Length - 1 ] |> List.forall (fun i -> digit i >= 0)) then None
            elif t.Length = 4 then Some(digit 1 * 17, digit 2 * 17, digit 3 * 17)
            else Some(digit 1 * 16 + digit 2, digit 3 * 16 + digit 4, digit 5 * 16 + digit 6)

    let isHex (s: string) : bool = (parseHex s).IsSome

    let private clamp01 (x: float) : float = if x < 0.0 then 0.0 elif x > 1.0 then 1.0 else x
    let private toByte (x: float) : int = int (Math.Floor(clamp01 x * 255.0 + 0.5))
    let private encode (linear: float) : float =
        if linear <= 0.0031308 then 12.92 * linear else 1.055 * Math.Pow(linear, 1.0 / 2.4) - 0.055
    let private decode (v: int) : float =
        let c = float v / 255.0
        if c <= 0.03928 then c / 12.92 else Math.Pow((c + 0.055) / 1.055, 2.4)

    /// Oklab/Oklch (Björn Ottosson) to sRGB, clipped to the gamut.
    let ofOklch (l: float, c: float, h: float) : int * int * int =
        let rad = h * Math.PI / 180.0
        let a, b = c * Math.Cos rad, c * Math.Sin rad
        let l' = l + 0.3963377774 * a + 0.2158037573 * b
        let m' = l - 0.1055613458 * a - 0.0638541728 * b
        let s' = l - 0.0894841775 * a - 1.2914855480 * b
        let lc, mc, sc = l' * l' * l', m' * m' * m', s' * s' * s'
        let r = 4.0767416621 * lc - 3.3077115913 * mc + 0.2309699292 * sc
        let g = -1.2684380046 * lc + 2.6097574011 * mc - 0.3413193965 * sc
        let bl = -0.0041960863 * lc - 0.7034186147 * mc + 1.7076147010 * sc
        toByte (encode (clamp01 r)), toByte (encode (clamp01 g)), toByte (encode (clamp01 bl))

    let ofHsl (h: float, s: float, l: float) : int * int * int =
        let hue = ((h % 360.0) + 360.0) % 360.0
        let chroma = (1.0 - abs (2.0 * l - 1.0)) * s
        let x = chroma * (1.0 - abs ((hue / 60.0) % 2.0 - 1.0))
        let m = l - chroma / 2.0
        let r, g, b =
            if hue < 60.0 then chroma, x, 0.0
            elif hue < 120.0 then x, chroma, 0.0
            elif hue < 180.0 then 0.0, chroma, x
            elif hue < 240.0 then 0.0, x, chroma
            elif hue < 300.0 then x, 0.0, chroma
            else chroma, 0.0, x
        toByte (r + m), toByte (g + m), toByte (b + m)

    /// The colour as sRGB, where it has one (not for Transparent, Inherit, CurrentColor, Var).
    let toRgb (c: Color) : (int * int * int) option =
        match c with
        | Hex s -> parseHex s
        | Rgb(r, g, b) -> Some(max 0 (min 255 r), max 0 (min 255 g), max 0 (min 255 b))
        | Hsl(h, s, l) -> Some(ofHsl (h, s, l))
        | Oklch(l, c, h) -> Some(ofOklch (l, c, h))
        | _ -> None

    let private hex2 (n: int) : string =
        let d = "0123456789abcdef"
        d.Substring(n / 16, 1) + d.Substring(n % 16, 1)

    let hexOfRgb (r: int, g: int, b: int) : string = "#" + hex2 r + hex2 g + hex2 b

    let toHex (c: Color) : string option = toRgb c |> Option.map hexOfRgb

    // ── contrast (WCAG 2.x) ──
    let luminance (r: int, g: int, b: int) : float = 0.2126 * decode r + 0.7152 * decode g + 0.0722 * decode b

    /// Contrast ratio 1..21; None when either colour has no sRGB value.
    let contrast (a: Color) (b: Color) : float option =
        match toRgb a, toRgb b with
        | Some x, Some y ->
            let la, lb = luminance x, luminance y
            Some((max la lb + 0.05) / (min la lb + 0.05))
        | _ -> None

    /// 4.5 : 1 (WCAG AA for normal text); true when a colour cannot be judged.
    let meetsAA (a: Color) (b: Color) : bool = match contrast a b with Some r -> r >= 4.5 | None -> true

    // ── adjusting (what Sass writes as color.adjust / color.scale in the hsl space) ──
    let toHsl (r: int, g: int, b: int) : float * float * float =
        let rf, gf, bf = float r / 255.0, float g / 255.0, float b / 255.0
        let mx, mn = max rf (max gf bf), min rf (min gf bf)
        let l = (mx + mn) / 2.0
        let d = mx - mn
        if d = 0.0 then 0.0, 0.0, l
        else
            let s = if l > 0.5 then d / (2.0 - mx - mn) else d / (mx + mn)
            let h =
                if mx = rf then ((gf - bf) / d + (if gf < bf then 6.0 else 0.0))
                elif mx = gf then (bf - rf) / d + 2.0
                else (rf - gf) / d + 4.0
            h * 60.0, s, l

    /// Lightness plus `delta` (-0.1 = ten points darker).
    let adjustLightness (delta: float) (c: Color) : Color =
        match toRgb c with
        | Some rgb ->
            let h, s, l = toHsl rgb
            Hex(hexOfRgb (ofHsl (h, s, clamp01 (l + delta))))
        | None -> c

    /// Lightness moved `amount` of the way to white (positive) or black (negative): Sass `color.scale($c, $lightness: 85%)`.
    let scaleLightness (amount: float) (c: Color) : Color =
        match toRgb c with
        | Some rgb ->
            let h, s, l = toHsl rgb
            let l2 = if amount >= 0.0 then l + (1.0 - l) * amount else l + l * amount
            Hex(hexOfRgb (ofHsl (h, s, clamp01 l2)))
        | None -> c

    /// The colour as CSS text.  For an email everything with an sRGB value becomes `#rrggbb`.
    let rec css (t: Target) (c: Color) : string =
        match c with
        | Transparent -> "transparent"
        | Inherit -> "inherit"
        | CurrentColor -> "currentColor"
        | Var(name, fallback) ->
            (match t, fallback with
             | Email, Some f -> css t f
             | Email, None -> "inherit"
             | Web, Some f -> "var(--" + name + ", " + css t f + ")"
             | Web, None -> "var(--" + name + ")")
        | Hex s when t = Web -> s.Trim().ToLowerInvariant()
        | Rgb(r, g, b) when t = Web -> "rgb(" + string r + ", " + string g + ", " + string b + ")"
        | Hsl(h, s, l) when t = Web -> "hsl(" + Num.fmt h + ", " + Num.fmt (s * 100.0) + "%, " + Num.fmt (l * 100.0) + "%)"
        | Oklch(l, ch, h) when t = Web -> "oklch(" + Num.fmt l + " " + Num.fmt ch + " " + Num.fmt h + ")"
        | _ -> (match toHex c with Some h -> h | None -> "inherit")
