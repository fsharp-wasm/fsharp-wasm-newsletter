namespace FSharp.Wasm.Ui

/// What a stylesheet is written for.  A web page takes modern CSS as it is; an email client does not (no custom properties, no
/// oklch, little beyond tables and inline styles), so the same typed values are lowered when the target is `Email`.
type Target =
    | Web
    | Email

module internal Num =
    /// A number as CSS writes it: `16`, `1.5`, `0.3333` (at most four decimals, no exponent, no trailing zeros).
    let fmt (f: float) : string =
        let negative = f < 0.0
        let n = int64 (System.Math.Floor(abs f * 10000.0 + 0.5))
        let whole = n / 10000L
        let frac = int (n % 10000L)
        let digits = string (10000 + frac)                      // "1" + four decimals
        let mutable fracText = digits.Substring 1
        while fracText.Length > 0 && fracText.EndsWith "0" do fracText <- fracText.Substring(0, fracText.Length - 1)
        let body = if fracText = "" then string whole else string whole + "." + fracText
        if negative && n <> 0L then "-" + body else body
