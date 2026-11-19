/// The three outputs of a document: the HTML email (tables, inline styles, Outlook conditionals), the web version (the framework's
/// real class names and stylesheet) and the plain-text alternative.  Pure functions of the document and the profile.
module Newsletter.Render

open System.Text
open Newsletter.Model
open Newsletter.Profile
open Newsletter.Markup
open FSharp.Wasm.Ui

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
// The email is built on the grid and the components of Foundation for Emails (FSharp.Wasm.Ui.Ink): every block is a row of one column
// (two for "Two columns"), the stylesheet is a function of the profile's tokens, and the inliner writes it into the elements.

let private inkSettings (p: Profile) : Ink.Settings =
    let c key = Hex(colorOf key p)
    let width = float (numOf "width" 320 900 p)
    { Ink.defaults with
        Primary = c "primary"; Secondary = c "secondary"
        // Foundation's `White` is the text of a button
        White = c "onPrimary"
        FontColor = c "text"; MediumGray = c "muted"; BodyBackground = c "canvas"; ContainerBackground = c "surface"
        FontFamily = fontOf p; FontSize = float (numOf "size" 10 28 p); GlobalWidth = width; Radius = float (numOf "radius" 0 40 p) }

/// Rules that are the newsletter's own, on top of Foundation's: the secondary button's label, the quote, the divider, the footer, and the
/// selection outline of the preview.
let private extraRules (p: Profile) (o: Options) : Rule list =
    let c key = Hex(colorOf key p)
    let rules =
        [ // Foundation sets headings in normal weight and lets text hyphenate; a newsletter wants bold headings and no broken words
          Style.rule [ "h1"; "h2"; "h3" ] [ Style.fontWeight FontWeight.Bold ]
          Style.rule [ "td"; "th" ] [ Style.raw "-webkit-hyphens" "manual"; Style.raw "-moz-hyphens" "manual"; Style.raw "hyphens" "manual" ]
          Style.rule [ "table.button.secondary table td a" ] [ Style.color (c "onSecondary") ]
          Style.rule [ "table.quote th.quote-inner" ] [ Style.raw "border-left" ("4px solid " + colorOf "primary" p); Style.padding4 (Px 4.0) Zero (Px 4.0) (Px 16.0) ]
          Style.rule [ "table.quote p" ] [ Style.fontSize (Em 1.125); Style.raw "font-style" "italic" ]
          Style.rule [ "table.quote p.quote-by" ] [ Style.fontSize (Em 0.875); Style.color (c "muted"); Style.raw "font-style" "normal" ]
          Style.rule [ "table.divider th" ] [ Style.raw "border-top" ("1px solid " + colorOf "border" p); Style.fontSize Zero; Style.lineHeightLen Zero; Style.height (Px 1.0) ]
          Style.rule [ "p.footer"; "p.footer a" ] [ Style.color (c "muted"); Style.fontSize (Px (max 11.0 (float (numOf "size" 10 28 p) - 3.0))) ] ]
    if o.Preview then
        rules @ [ Style.rule [ "[data-block]:hover" ] [ Style.raw "outline" "1px dashed #6c757d"; Style.raw "outline-offset" "-1px"; Style.raw "cursor" "pointer" ]
                  Style.rule [ "[data-selected]" ] [ Style.important (Style.raw "outline" "2px solid #0d6efd"); Style.raw "outline-offset" "-2px" ] ]
    else rules

let private alignClass (a: Align) : (string * string) list =
    match a with Left -> [] | Center -> [ Html.cls "text-center" ] | Right -> [ Html.cls "text-right" ]

let private paragraphsOf (text: string) (attrs: (string * string) list) : Node list =
    paragraphs text |> List.map (fun t -> Html.el "p" attrs [ Html.raw (inlineHtml "" t) ])

/// One block as a row of Foundation's grid.
let private emailBlock (p: Profile) (o: Options) (b: Block) : Node =
    let one (kids: Node list) = Ink.grid12 false [ (12, 12, kids) ]
    let marked (n: Node) =
        if o.Preview then Html.withAttrs ([ "data-block", string b.Id ] @ (if b.Id = o.Selected then [ "data-selected", "" ] else [])) n else n
    let width = numOf "width" 320 900 p
    marked
        (match b.Kind with
         | Heading -> one [ Html.el ("h" + string (clamp 1 3 b.Level)) (alignClass b.Align) [ Html.text b.Text ] ]
         | Paragraph -> one (paragraphsOf b.Text (alignClass b.Align))
         | Image ->
            let w = max 1 ((width - 2 * int (inkSettings p).Gutter) * clamp 10 100 b.Size / 100)
            let img =
                Html.el "img" ([ "src", safeUrl b.Url; "alt", b.Alt; "width", string w ] @ (match b.Align with Left -> [] | Center -> [ Html.cls "float-center" ] | Right -> [ Html.cls "float-right" ])) []
            one [ (if b.Link.Trim() = "" then img else Html.el "a" [ "href", safeUrl b.Link ] [ img ]) ]
         | Button ->
            let variant = (match b.Variant with Primary -> Ink.Primary | Secondary -> Ink.Secondary | Outline -> Ink.Outline)
            let btn = Ink.button variant Ink.Default false (safeUrl b.Url) [ Html.text b.Text ]
            one [ (match b.Align with Left -> btn | Center -> Html.withAttrs [ Html.cls "float-center"; "align", "center" ] btn | Right -> Html.withAttrs [ "align", "right" ] btn) ]
         | Quote ->
            let by = if b.Text2.Trim() = "" then [] else [ Html.el "p" [ Html.cls "quote-by" ] [ Html.text ("— " + b.Text2) ] ]
            one [ Html.el "table" [ "role", "presentation"; Html.cls "quote"; "border", "0"; "cellpadding", "0"; "cellspacing", "0" ]
                    [ Html.el "tbody" [] [ Html.el "tr" [] [ Html.el "th" [ Html.cls "quote-inner" ] ([ Html.el "p" [] [ Html.raw (inlineHtml "" b.Text) ] ] @ by) ] ] ] ]
         | Divider ->
            one [ Html.el "table" [ "role", "presentation"; Html.cls "divider"; "border", "0"; "cellpadding", "0"; "cellspacing", "0" ] [ Html.el "tbody" [] [ Html.el "tr" [] [ Html.el "th" [] [ Html.raw "&nbsp;" ] ] ] ] ]
         | Spacer -> Ink.spacer (float (clamp 4 120 b.Size))
         | Columns -> Ink.grid12 false [ (12, 6, paragraphsOf b.Text []); (12, 6, paragraphsOf b.Text2 []) ]
         | Footer ->
            let line (s: string) = if s.Trim() = "" then [] else [ Html.el "p" [ Html.cls "footer text-center" ] [ Html.raw (inlineHtml "" s) ] ]
            let unsub = Html.el "p" [ Html.cls "footer text-center" ] [ Html.el "a" [ "href", safeUrl (if b.Url.Trim() = "" then "{{unsubscribe_url}}" else b.Url) ] [ Html.text "Unsubscribe" ] ]
            one (line b.Text @ line b.Text2 @ [ unsub ]))

/// The HTML email: Foundation's grid and components, every style inlined, a hidden preheader.  `Preview` adds the block markers, the selection
/// outline and the click bridge of the designer; an export has none of it.
let email (o: Options) (p: Profile) (d: Document) : string =
    let body = [ Ink.container (d.Blocks |> List.map (emailBlock p o)) ]
    Ink.page (inkSettings p) (if d.Lang.Trim() = "" then "en" else d.Lang) (if d.Subject.Trim() = "" then d.Name else d.Subject) d.Preheader
        (extraRules p o) "" (if o.Preview then previewScript else "") body

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
