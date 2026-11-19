/// The three outputs of a document: the HTML email (tables, inline styles, Outlook conditionals), the web version (the framework's
/// real class names and stylesheet) and the plain-text alternative.  Pure functions of the document and the profile.
module Newsletter.Render

open System.Text
open Newsletter.Model
open Newsletter.Profile
open Newsletter.Markup

/// `Preview` adds what the designer needs (block markers, the selection outline, the click bridge); an export has none of it.
/// `Local` maps a stylesheet address to a local copy (the preview uses the copy that ships with the designer).
type Options = { Preview: bool; Selected: int; Local: string -> string }

let exportOptions : Options = { Preview = false; Selected = -1; Local = id }

// ── safe values: a profile can be imported, so no token reaches the output unchecked ──
let private colorOf (key: string) (p: Profile) : string =
    let v = token key p
    if Newsletter.Color.isColor v then v else (bootstrap.Tokens |> List.find (fun (k, _) -> k = key) |> snd)

let private fontOf (p: Profile) : string =
    let allowed (c: char) = System.Char.IsLetterOrDigit c || c = ' ' || c = ',' || c = '\'' || c = '-' || c = '.' || c = '_'
    let sb = StringBuilder()
    for c in token "font" p do
        if allowed c then sb.Append(c) |> ignore
    let cleaned = sb.ToString()
    if cleaned.Trim() = "" then token "font" bootstrap else cleaned

let private numOf (key: string) (lo: int) (hi: int) (p: Profile) : int = clamp lo hi (tokenInt key p)

let private style (pairs: (string * string) list) : string =
    pairs |> List.map (fun (k, v) -> k + ":" + v) |> String.concat ";"

let private joinClasses (xs: string list) : string = xs |> List.filter (fun x -> x <> "") |> String.concat " "

let private attr (name: string) (value: string) : string = " " + name + "=\"" + escape value + "\""

let private marker (o: Options) (b: Block) : string =
    if not o.Preview then ""
    else
        attr "data-block" (string b.Id)
        + (if b.Id = o.Selected then attr "data-selected" "" else "")

// The preview page talks to the designer through one message: "this block was clicked".  The preview runs in a sandboxed frame
// (scripts only, no access to the designer's page), so what the user's content contains cannot reach the designer.
let private previewHead : string =
    "<style>[data-block]{cursor:pointer}[data-block]:hover{outline:1px dashed #6c757d;outline-offset:-1px}"
    + "[data-selected]{outline:2px solid #0d6efd!important;outline-offset:-2px}</style>"

let private previewScript : string =
    "<script>addEventListener('click',function(e){var t=e.target.closest&&e.target.closest('[data-block]');"
    + "if(t){e.preventDefault();parent.postMessage({block:t.getAttribute('data-block')},'*')}});"
    + "addEventListener('scroll',function(){parent.postMessage({scroll:scrollY},'*')});"
    + "addEventListener('message',function(e){if(e.data&&typeof e.data.scroll==='number')scrollTo(0,e.data.scroll)});"
    + "parent.postMessage({ready:true},'*')</script>"

// ───────────────────────────── email ─────────────────────────────

let private pad = 24

let private emailBlock (p: Profile) (o: Options) (b: Block) : string =
    let text, muted, primary = colorOf "text" p, colorOf "muted" p, colorOf "primary" p
    let font, size, radius = fontOf p, numOf "size" 10 28 p, numOf "radius" 0 40 p
    let width = numOf "width" 320 900 p
    let ff = ("font-family", font)
    let align = alignKey b.Align
    let link = style [ "color", primary; "text-decoration", "underline" ]
    let cell (padding: string) (inner: string) =
        "<tr><td" + marker o b + attr "style" ("padding:" + padding) + ">" + inner + "</td></tr>\n"
    let para (px: int) (color: string) (s: string) =
        paragraphs s
        |> List.map (fun t ->
            "<p" + attr "style" (style [ "margin", "0 0 12px"; ff; "font-size", string px + "px"; "line-height", "1.6"; "color", color; "text-align", align ]) + ">"
            + inlineHtml (attr "style" link) t + "</p>")
        |> String.concat ""
    match b.Kind with
    | Heading ->
        let px = (match b.Level with 1 -> size * 2 | 2 -> size * 3 / 2 | _ -> size * 5 / 4)
        let tag = "h" + string (clamp 1 3 b.Level)
        cell "12px 24px 4px" ("<" + tag + attr "style" (style [ "margin", "0"; ff; "font-size", string px + "px"; "line-height", "1.25"; "font-weight", "700"; "color", text; "text-align", align ]) + ">" + escape b.Text + "</" + tag + ">")
    | Paragraph -> cell "8px 24px 0" (para size text b.Text)
    | Image ->
        let w = max 1 ((width - 2 * pad) * clamp 10 100 b.Size / 100)
        let img =
            "<img" + attr "src" (safeUrl b.Url) + attr "alt" b.Alt + attr "width" (string w)
            + attr "style" (style [ "display", "block"; "border", "0"; "width", "100%"; "max-width", string w + "px"; "height", "auto" ]) + ">"
        let inner = if b.Link.Trim() = "" then img else "<a" + attr "href" (safeUrl b.Link) + ">" + img + "</a>"
        cell "8px 24px" ("<table role=\"presentation\" border=\"0\" cellpadding=\"0\" cellspacing=\"0\"" + attr "align" align + "><tr><td>" + inner + "</td></tr></table>")
    | Button ->
        let bg, fg, border =
            match b.Variant with
            | Primary -> primary, colorOf "onPrimary" p, primary
            | Secondary -> colorOf "secondary" p, colorOf "onSecondary" p, colorOf "secondary" p
            | Outline -> "transparent", primary, primary
        let href = safeUrl b.Url
        let w = 48 + 10 * b.Text.Length
        let arc = if radius = 0 then 0 else min 50 (radius * 100 / 44)
        let vml =
            "<!--[if mso]><v:roundrect xmlns:v=\"urn:schemas-microsoft-com:vml\" xmlns:w=\"urn:schemas-microsoft-com:office:word\"" + attr "href" href
            + attr "style" ("height:44px;v-text-anchor:middle;width:" + string w + "px;") + attr "arcsize" (string arc + "%")
            + (if b.Variant = Outline then attr "strokecolor" border + " fillcolor=\"" + colorOf "surface" p + "\"" else " stroke=\"f\" fillcolor=\"" + bg + "\"")
            + "><w:anchorlock/><center" + attr "style" (style [ "color", fg; ff; "font-size", string size + "px"; "font-weight", "bold" ]) + ">" + escape b.Text + "</center></v:roundrect><![endif]-->"
        let anchor =
            "<a" + attr "href" href
            + attr "style" (style [ "display", "inline-block"; "padding", "11px 24px"; ff; "font-size", string size + "px"; "line-height", "20px"; "font-weight", "bold"
                                    "color", fg; "text-decoration", "none"; "border", "1px solid " + border; "border-radius", string radius + "px"; "background-color", bg ])
            + ">" + escape b.Text + "</a>"
        cell "12px 24px"
            ("<table role=\"presentation\" border=\"0\" cellpadding=\"0\" cellspacing=\"0\"" + attr "align" align + "><tr><td>"
             + vml + "<!--[if !mso]><!-->" + anchor + "<!--<![endif]--></td></tr></table>")
    | Quote ->
        let q = "<p" + attr "style" (style [ "margin", "0"; ff; "font-size", string (size * 9 / 8) + "px"; "line-height", "1.5"; "font-style", "italic"; "color", text ]) + ">" + inlineHtml (attr "style" link) b.Text + "</p>"
        let by =
            if b.Text2.Trim() = "" then ""
            else "<p" + attr "style" (style [ "margin", "8px 0 0"; ff; "font-size", string (max 12 (size - 2)) + "px"; "color", muted ]) + ">— " + escape b.Text2 + "</p>"
        cell "12px 24px" ("<table role=\"presentation\" width=\"100%\" border=\"0\" cellpadding=\"0\" cellspacing=\"0\"><tr><td" + attr "style" ("border-left:4px solid " + primary + ";padding:4px 0 4px 16px") + ">" + q + by + "</td></tr></table>")
    | Divider ->
        cell "12px 24px" ("<table role=\"presentation\" width=\"100%\" border=\"0\" cellpadding=\"0\" cellspacing=\"0\"><tr><td" + attr "style" ("border-top:1px solid " + colorOf "border" p + ";font-size:0;line-height:0;height:1px") + ">&nbsp;</td></tr></table>")
    | Spacer ->
        let h = string (clamp 4 120 b.Size)
        "<tr><td" + marker o b + attr "style" ("height:" + h + "px;line-height:" + h + "px;font-size:1px") + attr "height" h + ">&nbsp;</td></tr>\n"
    | Columns ->
        let col (side: string) (s: string) =
            "<td class=\"stack\" width=\"50%\" valign=\"top\"" + attr "style" ("padding-" + side + ":12px") + ">" + para size text s + "</td>"
        cell "8px 24px 0" ("<table role=\"presentation\" width=\"100%\" border=\"0\" cellpadding=\"0\" cellspacing=\"0\"><tr>" + col "right" b.Text + col "left" b.Text2 + "</tr></table>")
    | Footer ->
        let small = max 11 (size - 3)
        let line (s: string) = if s.Trim() = "" then "" else "<p" + attr "style" (style [ "margin", "0 0 6px"; ff; "font-size", string small + "px"; "line-height", "1.5"; "color", muted; "text-align", align ]) + ">" + inlineHtml (attr "style" (style [ "color", muted; "text-decoration", "underline" ])) s + "</p>"
        let unsub =
            "<p" + attr "style" (style [ "margin", "0"; ff; "font-size", string small + "px"; "text-align", align ]) + "><a" + attr "href" (safeUrl (if b.Url.Trim() = "" then "{{unsubscribe_url}}" else b.Url))
            + attr "style" (style [ "color", muted; "text-decoration", "underline" ]) + ">Unsubscribe</a></p>"
        cell "20px 24px 24px" (line b.Text + line b.Text2 + unsub)

/// The HTML email: a centred table of the profile's width, every style inline, MSO conditionals for Outlook, a hidden preheader.
let email (o: Options) (p: Profile) (d: Document) : string =
    let width = numOf "width" 320 900 p
    let canvas, surface = colorOf "canvas" p, colorOf "surface" p
    let sb = StringBuilder()
    let add (s: string) = sb.Append(s) |> ignore
    add "<!DOCTYPE html>\n"
    add ("<html" + attr "lang" (if d.Lang.Trim() = "" then "en" else d.Lang) + " xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:v=\"urn:schemas-microsoft-com:vml\" xmlns:o=\"urn:schemas-microsoft-com:office:office\">\n<head>\n")
    add "<meta charset=\"utf-8\">\n<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n<meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\">\n"
    add "<meta name=\"x-apple-disable-message-reformatting\">\n<meta name=\"format-detection\" content=\"telephone=no, date=no, address=no, email=no\">\n"
    add ("<title>" + escape (if d.Subject.Trim() = "" then d.Name else d.Subject) + "</title>\n")
    add "<!--[if mso]><noscript><xml><o:OfficeDocumentSettings><o:PixelsPerInch>96</o:PixelsPerInch></o:OfficeDocumentSettings></xml></noscript><![endif]-->\n"
    add "<style>body{margin:0;padding:0;-webkit-text-size-adjust:100%;-ms-text-size-adjust:100%}table,td{mso-table-lspace:0;mso-table-rspace:0}img{-ms-interpolation-mode:bicubic}"
    add "@media only screen and (max-width:620px){.container{width:100%!important}.stack{display:block!important;width:100%!important;padding:0!important}}</style>\n"
    if o.Preview then add (previewHead + "\n")
    add "</head>\n"
    add ("<body" + attr "style" (style [ "margin", "0"; "padding", "0"; "background-color", canvas ]) + ">\n")
    if d.Preheader.Trim() <> "" then
        add ("<div style=\"display:none;max-height:0;overflow:hidden;mso-hide:all;font-size:1px;line-height:1px;color:" + canvas + ";opacity:0\">" + escape d.Preheader + String.replicate 40 "&zwnj;&nbsp;" + "</div>\n")
    add ("<table role=\"presentation\" width=\"100%\" border=\"0\" cellpadding=\"0\" cellspacing=\"0\"" + attr "bgcolor" canvas + "><tr><td align=\"center\" style=\"padding:24px 12px\">\n")
    add ("<!--[if mso]><table role=\"presentation\" width=\"" + string width + "\" align=\"center\" cellpadding=\"0\" cellspacing=\"0\"><tr><td><![endif]-->\n")
    add ("<table role=\"presentation\" class=\"container\" border=\"0\" cellpadding=\"0\" cellspacing=\"0\" width=\"" + string width + "\"" + attr "bgcolor" surface
         + attr "style" ("width:100%;max-width:" + string width + "px;background-color:" + surface + ";border-radius:" + string (numOf "radius" 0 40 p) + "px") + ">\n")
    for b in d.Blocks do add (emailBlock p o b)
    add "</table>\n<!--[if mso]></td></tr></table><![endif]-->\n</td></tr></table>\n"
    if o.Preview then add (previewScript + "\n")
    add "</body>\n</html>\n"
    sb.ToString()

// ───────────────────────────── web version ─────────────────────────────

let private webBlock (p: Profile) (o: Options) (b: Block) : string =
    let c role = classes role p
    let alignClass = c (alignKey b.Align)
    let cls (xs: string list) = let j = joinClasses xs in if j = "" then "" else attr "class" j
    let m = marker o b
    let paras (klass: string) (s: string) = paragraphs s |> List.map (fun t -> "<p" + cls [ klass; alignClass ] + (if klass = c "p" then m else "") + ">" + inlineHtml "" t + "</p>") |> String.concat "\n"
    match b.Kind with
    | Heading -> let lv = string (clamp 1 3 b.Level) in "<h" + lv + cls [ c ("h" + lv); alignClass ] + m + ">" + escape b.Text + "</h" + lv + ">"
    | Paragraph -> paras (c "p") b.Text
    | Image ->
        let img = "<img" + attr "src" (safeUrl b.Url) + attr "alt" b.Alt + cls [ c "image" ] + attr "style" ("width:" + string (clamp 10 100 b.Size) + "%;height:auto") + ">"
        "<div" + cls [ alignClass ] + m + ">" + (if b.Link.Trim() = "" then img else "<a" + attr "href" (safeUrl b.Link) + ">" + img + "</a>") + "</div>"
    | Button ->
        let role = (match b.Variant with Primary -> "button-primary" | Secondary -> "button-secondary" | Outline -> "button-outline")
        "<div" + cls [ alignClass ] + m + "><a" + attr "href" (safeUrl b.Url) + cls [ c role ] + ">" + escape b.Text + "</a></div>"
    | Quote ->
        "<blockquote" + cls [ c "quote" ] + m + "><p>" + inlineHtml "" b.Text + "</p>"
        + (if b.Text2.Trim() = "" then "" else "<footer>— " + escape b.Text2 + "</footer>") + "</blockquote>"
    | Divider -> "<hr" + cls [ c "divider" ] + m + ">"
    | Spacer -> "<div" + attr "style" ("height:" + string (clamp 4 120 b.Size) + "px") + attr "aria-hidden" "true" + m + "></div>"
    | Columns ->
        let col (s: string) = "<div" + cls [ c "col" ] + ">" + (paragraphs s |> List.map (fun t -> "<p>" + inlineHtml "" t + "</p>") |> String.concat "") + "</div>"
        "<div" + cls [ c "row" ] + m + ">" + col b.Text + col b.Text2 + "</div>"
    | Footer ->
        let line (s: string) = if s.Trim() = "" then "" else "<p>" + inlineHtml "" s + "</p>"
        "<footer" + cls [ c "footer"; alignClass ] + m + ">" + line b.Text + line b.Text2
        + "<p><a" + attr "href" (safeUrl (if b.Url.Trim() = "" then "{{unsubscribe_url}}" else b.Url)) + ">Unsubscribe</a></p></footer>"

/// The web version: semantic HTML marked up with the profile's class names, the framework's stylesheet linked.
let web (o: Options) (p: Profile) (d: Document) : string =
    let sb = StringBuilder()
    let add (s: string) = sb.Append(s) |> ignore
    add "<!DOCTYPE html>\n"
    add ("<html" + attr "lang" (if d.Lang.Trim() = "" then "en" else d.Lang) + ">\n<head>\n<meta charset=\"utf-8\">\n<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n")
    add ("<title>" + escape (if d.Subject.Trim() = "" then d.Name else d.Subject) + "</title>\n")
    if p.Stylesheet.Trim() <> "" then add ("<link rel=\"stylesheet\"" + attr "href" (safeUrl (o.Local p.Stylesheet)) + ">\n")
    if p.Script.Trim() <> "" then add ("<script" + attr "src" (safeUrl p.Script) + "></script>\n")
    if o.Preview then add (previewHead + "\n")
    add "</head>\n<body>\n"
    add ("<main" + (let k = classes "container" p in if k = "" then "" else attr "class" k) + ">\n")
    for b in d.Blocks do add (webBlock p o b + "\n")
    add "</main>\n"
    if o.Preview then add (previewScript + "\n")
    add "</body>\n</html>\n"
    sb.ToString()

// ───────────────────────────── plain text ─────────────────────────────

let private textBlock (b: Block) : string =
    let paras (s: string) = paragraphs s |> List.map inlineText |> String.concat "\n\n"
    match b.Kind with
    | Heading ->
        let t = b.Text.Trim()
        (match b.Level with
         | 1 -> t.ToUpperInvariant() + "\n" + String.replicate (max 3 t.Length) "="
         | 2 -> t + "\n" + String.replicate (max 3 t.Length) "-"
         | _ -> t)
    | Paragraph -> paras b.Text
    | Image -> "[" + (if b.Alt.Trim() = "" then "Image" else b.Alt.Trim()) + "]" + (if b.Link.Trim() = "" then "" else " " + safeUrl b.Link)
    | Button -> b.Text.Trim() + ": " + safeUrl b.Url
    | Quote -> "> " + inlineText (b.Text.Trim()) + (if b.Text2.Trim() = "" then "" else "\n> — " + b.Text2.Trim())
    | Divider -> String.replicate 40 "-"
    | Spacer -> ""
    | Columns -> paras b.Text + "\n\n" + paras b.Text2
    | Footer ->
        let lines = [ inlineText (b.Text.Trim()); b.Text2.Trim(); "Unsubscribe: " + safeUrl (if b.Url.Trim() = "" then "{{unsubscribe_url}}" else b.Url) ]
        "--\n" + (lines |> List.filter (fun s -> s <> "") |> String.concat "\n")

/// The plain-text alternative of the email (the second part of a multipart message).
let text (d: Document) : string =
    d.Blocks |> List.map textBlock |> List.filter (fun s -> s <> "") |> String.concat "\n\n" |> fun s -> s + "\n"
