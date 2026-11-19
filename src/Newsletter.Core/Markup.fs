/// Text to HTML: escaping, links that cannot run code, and the small inline markup of the editor (**bold**, _italic_,
/// [label](address)).  Used by every renderer, so the three outputs agree on what a paragraph means.
module Newsletter.Markup

open System.Text

let escape (s: string) : string = FSharp.Wasm.Ui.Html.escape s

/// An address that is safe to put in `href`/`src`: http(s), mailto, tel, relative and #anchors, and the merge tags of sending
/// services (`{{unsubscribe_url}}`).  Anything else (`javascript:`, `data:`) becomes `#`.
let safeUrl (s: string) : string =
    let t = s.Trim()
    let l = t.ToLowerInvariant()
    let colon = l.IndexOf ':'
    let mutable firstSep = l.Length
    for i in l.Length - 1 .. -1 .. 0 do
        if l.[i] = '/' || l.[i] = '?' || l.[i] = '#' then firstSep <- i
    let hasScheme = colon > 0 && colon < firstSep
    if t = "" then "#"
    elif l.StartsWith "{{" || l.StartsWith "http://" || l.StartsWith "https://" || l.StartsWith "mailto:" || l.StartsWith "tel:" then t
    elif not hasScheme then t
    else "#"

/// Paragraph text split at blank lines (a trailing/leading blank is dropped).
let paragraphs (s: string) : string list =
    s.Replace("\r\n", "\n").Split([| "\n\n" |], System.StringSplitOptions.None)
    |> Array.map (fun p -> p.Trim())
    |> Array.filter (fun p -> p <> "")
    |> Array.toList

type private Piece = Plain of string | Strong of string | Emph of string | Link of label: string * url: string

/// Index of the `)` that closes a link address starting at `from` (addresses may contain balanced parentheses), or -1.
let private closing (s: string) (from: int) : int =
    let mutable depth = 1
    let mutable i = from
    let mutable found = -1
    while found < 0 && i < s.Length do
        if s.[i] = '(' then depth <- depth + 1
        elif s.[i] = ')' then
            depth <- depth - 1
            if depth = 0 then found <- i
        i <- i + 1
    found

/// Splits the text into plain and marked-up pieces.
let private pieces (s: string) : Piece list =
    let out = ResizeArray<Piece>()
    let buf = StringBuilder()
    let flush () = if buf.Length > 0 then (out.Add(Plain(buf.ToString())); buf.Clear() |> ignore)
    let n = s.Length
    let mutable i = 0
    while i < n do
        let rest = s.Substring i
        if rest.StartsWith "**" && rest.IndexOf("**", 2) > 2 then
            let e = rest.IndexOf("**", 2)
            flush ()
            out.Add(Strong(rest.Substring(2, e - 2)))
            i <- i + e + 2
        elif rest.StartsWith "_" && rest.IndexOf('_', 1) > 1 && (i = 0 || s.[i - 1] = ' ' || s.[i - 1] = '\n') then
            let e = rest.IndexOf('_', 1)
            flush ()
            out.Add(Emph(rest.Substring(1, e - 1)))
            i <- i + e + 1
        elif rest.StartsWith "[" && rest.IndexOf("](") > 1 && closing rest (rest.IndexOf("](") + 2) > 0 then
            let m = rest.IndexOf("](")
            let e = closing rest (m + 2)
            flush ()
            out.Add(Link(rest.Substring(1, m - 1), rest.Substring(m + 2, e - m - 2)))
            i <- i + e + 1
        else
            buf.Append(s.[i]) |> ignore
            i <- i + 1
    flush ()
    List.ofSeq out

/// Inline markup to HTML; `linkAttrs` is what goes into every `<a …>` (a style for email, a class for the web version).
let inlineHtml (linkAttrs: string) (s: string) : string =
    pieces s
    |> List.map (fun p ->
        match p with
        | Plain t -> (escape t).Replace("\n", "<br>")
        | Strong t -> "<strong>" + escape t + "</strong>"
        | Emph t -> "<em>" + escape t + "</em>"
        | Link(label, url) -> "<a href=\"" + escape (safeUrl url) + "\"" + (if linkAttrs = "" then "" else " " + linkAttrs) + ">" + escape label + "</a>")
    |> String.concat ""

/// The same text for the plain-text alternative: links as `label (address)`, markers removed.
let inlineText (s: string) : string =
    pieces s
    |> List.map (fun p ->
        match p with
        | Plain t | Strong t | Emph t -> t
        | Link(label, url) -> label + " (" + safeUrl url + ")")
    |> String.concat ""
