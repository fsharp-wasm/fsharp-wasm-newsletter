namespace FSharp.Wasm.Ui

open System.Text

/// The grid and the components of **Foundation for Emails** (ZURB; MIT, Copyright (c) 2017 Foundation, inc.; the licence text is in
/// licenses/foundation-for-emails-LICENSE.md and is referenced in NOTICE), written as
/// typed F# instead of Sass and Inky: the settings are a record, the stylesheet is a function of them, the markup is built from
/// functions, and the inliner that Foundation runs as a Node build step is `Inline.apply`.  The rules, the numbers and the table
/// structure follow Foundation's `scss/` (the 12-column grid with fixed widths on large screens and percentages below the breakpoint,
/// nested tables with an expander, the table button, the callout, spacer, alignment); what changed is the language they are written in.
module Ink =
    type Settings =
        { Primary: Color
          Secondary: Color
          Success: Color
          Warning: Color
          Alert: Color
          LightGray: Color
          MediumGray: Color
          DarkGray: Color
          Black: Color
          White: Color
          BodyBackground: Color
          ContainerBackground: Color
          FontColor: Color
          FontFamily: string
          FontSize: float
          /// the width of the container in pixels
          GlobalWidth: float
          Gutter: float
          Padding: float
          Margin: float
          Radius: float
          ParagraphMargin: float
          CalloutPadding: float }

    /// Foundation's own defaults.
    let defaults : Settings =
        { Primary = Hex "#2199e8"; Secondary = Hex "#777777"; Success = Hex "#3adb76"; Warning = Hex "#ffae00"; Alert = Hex "#ec5840"
          LightGray = Hex "#f3f3f3"; MediumGray = Hex "#cacaca"; DarkGray = Hex "#8a8a8a"; Black = Hex "#0a0a0a"; White = Hex "#fefefe"
          BodyBackground = Hex "#f3f3f3"; ContainerBackground = Hex "#fefefe"; FontColor = Hex "#0a0a0a"
          FontFamily = "Helvetica, Arial, sans-serif"; FontSize = 16.0
          GlobalWidth = 580.0; Gutter = 16.0; Padding = 16.0; Margin = 16.0; Radius = 3.0; ParagraphMargin = 10.0; CalloutPadding = 10.0 }

    let breakpoint (s: Settings) : float = s.GlobalWidth + s.Gutter

    let private columnsCount = 12

    // ───────────── the stylesheet ─────────────
    let private sty (selectors: string list) (decls: Decl list) : Rule = Style.rule selectors decls
    let private px (v: float) : Length = Px v
    /// Outlook reads `Margin`, other clients `margin`: both are written.
    let private zeroMargins : Decl list = [ Style.margin Zero; Style.raw "Margin" "0" ]
    let private cross (parents: string list) (children: string list) : string list = parents |> List.collect (fun p -> children |> List.map (fun c -> p + " " + c))
    let private bothCells (suffix: string) : string list = [ "td" + suffix; "th" + suffix ]
    let private columnCells (suffix: string) : string list = [ "td.columns" + suffix; "td.column" + suffix; "th.columns" + suffix; "th.column" + suffix ]

    let private widthPx (s: Settings) (i: int) : float = s.GlobalWidth / float columnsCount * float i - s.Gutter
    let private widthPct (i: int) : Length = Pct(System.Math.Floor(float i / float columnsCount * 100.0 * 1000000.0) / 1000000.0)

    /// The resets that email clients need (Foundation's `_normalize.scss`): table defaults, no auto-sizing of text, images that fill.
    let private normalize : Rule list =
        [ sty [ "#outlook a" ] [ Style.padding Zero ]
          sty [ "body" ]
              [ Style.important (Style.width (Pct 100.0)); Style.minWidth (Pct 100.0); Style.raw "-webkit-text-size-adjust" "100%"; Style.raw "-ms-text-size-adjust" "100%"
                Style.margin Zero; Style.raw "Margin" "0"; Style.padding Zero; Style.boxSizing true ]
          sty [ ".ExternalClass" ] [ Style.width (Pct 100.0) ]
          sty [ ".ExternalClass"; ".ExternalClass p"; ".ExternalClass span"; ".ExternalClass font"; ".ExternalClass td"; ".ExternalClass th"; ".ExternalClass div" ] [ Style.lineHeightLen (Pct 100.0) ]
          sty [ "#backgroundTable" ] [ Style.margin Zero; Style.raw "Margin" "0"; Style.padding Zero; Style.important (Style.width (Pct 100.0)); Style.important (Style.lineHeightLen (Pct 100.0)) ]
          sty [ "img" ] [ Style.raw "outline" "none"; Style.textDecoration TextDecoration.NoDecoration; Style.raw "-ms-interpolation-mode" "bicubic"; Style.width Auto; Style.maxWidth (Pct 100.0); Style.raw "clear" "both"; Style.display Display.Block ]
          sty [ "center" ] [ Style.width (Pct 100.0) ]
          sty [ "a img" ] [ Style.borderNone ]
          sty [ "table" ] [ Style.raw "border-spacing" "0"; Style.borderCollapse true ]
          sty [ "td"; "th" ]
              [ Style.raw "word-wrap" "break-word"; Style.raw "-webkit-hyphens" "auto"; Style.raw "-moz-hyphens" "auto"; Style.raw "hyphens" "auto"
                Style.important (Style.borderCollapse true); Style.boxSizing true ]
          sty [ "table"; "tr"; "td"; "th" ] [ Style.padding Zero; Style.verticalAlign VerticalAlign.Top; Style.textAlign TextAlign.Left ]
          sty [ ".wrapper" ] [ Style.width (Pct 100.0) ] ]

    let private typography (s: Settings) : Rule list =
        let headings = [ "h1"; "h2"; "h3"; "h4"; "h5"; "h6" ]
        let size (factor: float) = px (System.Math.Floor(s.FontSize * factor))
        [ sty ([ "body"; "table.body" ] @ headings @ [ "p"; "td"; "th" ])
              ([ Style.color s.FontColor; Style.fontFamily s.FontFamily; Style.fontWeight FontWeight.Normal; Style.padding Zero ] @ zeroMargins @ [ Style.textAlign TextAlign.Left; Style.lineHeightLen (Pct 130.0) ])
          sty [ "a" ] [ Style.color s.FontColor; Style.fontFamily s.FontFamily; Style.fontWeight FontWeight.Normal; Style.padding Zero; Style.textAlign TextAlign.Left; Style.lineHeightLen (Pct 130.0) ]
          sty headings [ Style.color Inherit; Style.raw "word-wrap" "normal"; Style.marginBottom (px 10.0); Style.raw "Margin-bottom" "10px" ]
          sty [ "h1" ] [ Style.fontSize (size 2.125) ]
          sty [ "h2" ] [ Style.fontSize (size 1.875) ]
          sty [ "h3" ] [ Style.fontSize (size 1.75) ]
          sty [ "h4" ] [ Style.fontSize (size 1.5) ]
          sty [ "h5" ] [ Style.fontSize (size 1.2) ]
          sty [ "h6" ] [ Style.fontSize (size 1.125) ]
          sty [ "body"; "table.body"; "p"; "td"; "th" ] [ Style.fontSize (px s.FontSize); Style.lineHeightLen (Pct 130.0) ]
          sty [ "p" ] [ Style.marginBottom (px s.ParagraphMargin); Style.raw "Margin-bottom" (Style.lengthCss (px s.ParagraphMargin)) ]
          sty [ "p.lead" ] [ Style.fontSize (px (s.FontSize * 1.25)); Style.lineHeightLen (Pct 160.0) ]
          sty [ "a" ] [ Style.color s.Primary; Style.textDecoration TextDecoration.NoDecoration ]
          sty [ "a:hover"; "a:active" ] [ Style.color (Color.adjustLightness -0.1 s.Primary) ]
          sty [ "small" ] [ Style.fontSize (Pct 80.0); Style.color s.MediumGray ] ]

    let private gridRules (s: Settings) : Rule list =
        let g = s.Gutter
        let nest = [ "td.columns"; "td.column"; "th.columns"; "th.column" ]
        let perColumn : Rule list =
            [ for i in 1 .. columnsCount do
                let w = widthPx s i
                yield sty (bothCells (".large-" + string i)) [ Style.width (px w); Style.paddingLeft (px (g / 2.0)); Style.paddingRight (px (g / 2.0)) ]
                yield sty (bothCells (".large-" + string i + ".first")) [ Style.paddingLeft (px g) ]
                yield sty (bothCells (".large-" + string i + ".last")) [ Style.paddingRight (px g) ]
                // collapsed rows: no gutters
                yield sty (bothCells (".large-" + string i + ":not([class*=large-offset])") |> List.map (fun x -> ".collapse > tbody > tr > " + x))
                          [ Style.paddingRight Zero; Style.paddingLeft Zero; Style.width (px (w + g)) ]
                yield sty ((bothCells (".large-" + string i + ".first")) @ (bothCells (".large-" + string i + ".last")) |> List.map (fun x -> ".collapse > tbody > tr > " + x))
                          [ Style.width (px (w + g * 1.5)) ]
                yield sty (cross [ ".body .columns"; ".body .column" ] (bothCells (".large-" + string i))) [ Style.width (widthPct i) ] ]
        let offsets : Rule list =
            [ for i in 1 .. columnsCount - 1 do
                let sfx = ".large-offset-" + string i
                yield sty ((bothCells sfx) @ (bothCells (sfx + ".first")) @ (bothCells (sfx + ".last"))) [ Style.paddingLeft (px (widthPx s i + g * 2.0)) ] ]
        [ sty [ "table.body" ] [ Style.background s.BodyBackground; Style.height (Pct 100.0); Style.width (Pct 100.0) ]
          sty [ "table.container" ] ([ Style.background s.ContainerBackground; Style.width (px s.GlobalWidth); Style.margin4 Zero Auto Zero Auto; Style.raw "Margin" "0 auto"; Style.textAlign TextAlign.Inherit ])
          sty [ "table.row" ] [ Style.padding Zero; Style.width (Pct 100.0); Style.raw "position" "relative" ]
          sty [ "table.spacer" ] [ Style.width (Pct 100.0) ]
          sty [ "table.spacer td" ] [ Style.raw "mso-line-height-rule" "exactly" ]
          sty [ "table.container table.row" ] [ Style.display Display.Table ]
          sty nest ([ Style.margin4 Zero Auto Zero Auto; Style.raw "Margin" "0 auto"; Style.paddingLeft (px g); Style.paddingBottom (px s.Padding) ])
          // nested columns must not double the padding
          sty (cross nest [ ".column.first"; ".columns.first" ]) [ Style.important (Style.paddingLeft Zero) ]
          sty (cross nest [ ".column.last"; ".columns.last" ]) [ Style.important (Style.paddingRight Zero) ]
          sty (cross nest [ ".column:not([class*=large-offset])"; ".columns:not([class*=large-offset])" ]) [ Style.important (Style.paddingLeft Zero); Style.important (Style.paddingRight Zero) ]
          sty (columnCells ".last") [ Style.paddingRight (px g) ]
          sty (cross nest [ "table" ]) [ Style.width (Pct 100.0) ]
          sty (cross nest [ "table.button" ]) [ Style.width Auto ]
          sty (cross nest [ "table.button.expand"; "table.button.expanded" ]) [ Style.width (Pct 100.0) ] ]
        @ perColumn
        @ offsets
        @ [ sty (bothCells ".expander") [ Style.visibility Visibility.Hidden; Style.width Zero; Style.important (Style.padding Zero) ] ]

    let private small (s: Settings) : Rule =
        let g = s.Gutter
        let perColumn : Rule list =
            [ for i in 1 .. columnsCount ->
                sty (bothCells (".small-" + string i)) [ Style.important (Style.display Display.InlineBlock); Style.important (Style.width (widthPct i)) ] ]
        Style.mediaMaxWidth (px (breakpoint s))
            ([ sty [ "table.body img" ] [ Style.width Auto; Style.height Auto ]
               sty [ "table.body center" ] [ Style.important (Style.minWidth Zero) ]
               sty [ "table.body .container" ] [ Style.important (Style.width (Pct 95.0)) ]
               sty [ "table.body .columns"; "table.body .column" ]
                   [ Style.important (Style.height Auto); Style.boxSizing true; Style.important (Style.paddingLeft (px g)); Style.important (Style.paddingRight (px g)) ]
               sty [ "table.body .collapse > tbody > tr > .columns"; "table.body .collapse > tbody > tr > .column" ]
                   [ Style.important (Style.paddingLeft Zero); Style.important (Style.paddingRight Zero) ] ]
             @ perColumn
             @ [ sty [ ".columns td.small-12"; ".column td.small-12"; ".columns th.small-12"; ".column th.small-12" ]
                     [ Style.important (Style.display Display.Block); Style.important (Style.width (Pct 100.0)) ]
                 sty [ "table.body table.columns td.expander"; "table.body table.columns th.expander" ] [ Style.important (Style.display Display.NoDisplay) ]
                 sty [ "table.button.small-expand"; "table.button.small-expanded" ] [ Style.important (Style.width (Pct 100.0)) ]
                 sty [ "table.button.small-expand table a"; "table.button.small-expanded table a" ]
                     [ Style.important (Style.textAlign TextAlign.Center); Style.important (Style.width (Pct 100.0)); Style.important (Style.paddingLeft Zero); Style.important (Style.paddingRight Zero) ]
                 sty [ "th.callout-inner" ] [ Style.important (Style.padding (px s.CalloutPadding)) ]
                 sty [ ".small-float-center" ] [ Style.important (Style.margin4 Zero Auto Zero Auto); Style.important (Style.float' Float.NoFloat); Style.important (Style.textAlign TextAlign.Center) ]
                 sty [ ".small-text-center" ] [ Style.important (Style.textAlign TextAlign.Center) ]
                 sty [ ".small-text-left" ] [ Style.important (Style.textAlign TextAlign.Left) ]
                 sty [ ".small-text-right" ] [ Style.important (Style.textAlign TextAlign.Right) ] ])

    let private buttonRules (s: Settings) : Rule list =
        let sized (cls: string) (padding: Decl) (size: float) =
            sty [ "table.button." + cls + " table a" ] [ padding; Style.fontSize (px size) ]
        let variant (cls: string) (c: Color) (hover: Color) =
            [ sty [ "table.button." + cls + " table td" ] [ Style.background c; Style.border Zero BorderStyle.Solid c ]
              sty [ "table.button." + cls + " table a" ] [ Style.border Zero BorderStyle.Solid c ]
              sty [ "table.button." + cls + ":hover table td" ] [ Style.background hover; Style.raw "border-color" (Color.css Email hover) ]
              sty [ "table.button." + cls + ":hover table a" ] [ Style.border Zero BorderStyle.Solid hover ] ]
        [ sty [ "table.button" ] [ Style.padding Zero; Style.width Auto; Style.margin4 Zero Zero (px s.Margin) Zero; Style.raw "Margin" (Style.lengthCss Zero + " 0 " + Style.lengthCss (px s.Margin) + " 0") ]
          sty [ "table.button table td" ] [ Style.textAlign TextAlign.Left; Style.color s.White; Style.background s.Primary; Style.border (px 2.0) BorderStyle.Solid s.Primary ]
          sty [ "table.button table td a" ]
              [ Style.fontFamily s.FontFamily; Style.fontSize (px s.FontSize); Style.fontWeight FontWeight.Bold; Style.color s.White; Style.textDecoration TextDecoration.NoDecoration
                Style.textAlign TextAlign.Left; Style.display Display.InlineBlock; Style.paddingXY (px 8.0) (px 16.0); Style.border Zero BorderStyle.Solid s.Primary; Style.borderRadius (px s.Radius) ]
          sty [ "table.button.radius table td" ] [ Style.borderRadius (px s.Radius); Style.borderNone ]
          sty [ "table.button.rounded table td" ] [ Style.borderRadius (px 500.0); Style.borderNone ]
          sty [ "table.button:not(.expand):not(.expanded) table" ] [ Style.width Auto ]
          sty [ "table.button:hover table td"; "table.button:active table td" ]
              [ Style.background (Color.adjustLightness -0.1 s.Primary); Style.raw "border-color" (Color.css Email (Color.adjustLightness -0.1 s.Primary)); Style.color s.White ]
          sized "tiny" (Style.paddingXY (px 8.0) (px 16.0)) 10.0
          sized "small" (Style.paddingXY (px 10.0) (px 20.0)) 12.0
          sized "large" (Style.paddingXY (px 10.0) (px 20.0)) 20.0
          sty [ "table.button.expand"; "table.button.expanded" ] [ Style.width (Pct 100.0) ]
          sty [ "table.button.expand table"; "table.button.expanded table" ] [ Style.width (Pct 100.0) ]
          sty [ "table.button.expand table a"; "table.button.expanded table a" ] [ Style.textAlign TextAlign.Center; Style.width (Pct 100.0); Style.paddingLeft Zero; Style.paddingRight Zero ] ]
        @ variant "secondary" s.Secondary (Color.adjustLightness 0.1 s.Secondary)
        @ variant "success" s.Success (Color.adjustLightness -0.1 s.Success)
        @ variant "warning" s.Warning (Color.adjustLightness -0.1 s.Warning)
        @ variant "alert" s.Alert (Color.adjustLightness -0.1 s.Alert)
        @ [ sty [ "table.button.outline table td" ] [ Style.background Transparent; Style.border (px 2.0) BorderStyle.Solid s.Primary ]
            sty [ "table.button.outline table td a" ] [ Style.color s.Primary; Style.border Zero BorderStyle.Solid s.Primary ]
            sty [ "table.button.outline:hover table td" ] [ Style.background (Color.scaleLightness 0.85 s.Primary) ] ]

    let private calloutRules (s: Settings) : Rule list =
        let border (c: Color) = Style.border (px 1.0) BorderStyle.Solid (Color.adjustLightness -0.2 c)
        let tone (cls: string) (c: Color) =
            sty [ "th.callout-inner." + cls ] [ Style.background (Color.scaleLightness 0.85 c); border c; Style.color s.Black ]
        [ sty [ "table.callout" ] [ Style.marginBottom (px s.Margin); Style.raw "Margin-bottom" (Style.lengthCss (px s.Margin)) ]
          sty [ "th.callout-inner" ] [ Style.width (Pct 100.0); border s.White; Style.padding (px s.CalloutPadding); Style.background s.White ]
          tone "primary" s.Primary
          tone "secondary" s.Secondary
          tone "success" s.Success
          tone "warning" s.Warning
          tone "alert" s.Alert ]

    let private alignment : Rule list =
        let elements = [ "table"; "th"; "td"; "h1"; "h2"; "h3"; "h4"; "h5"; "h6"; "p"; "span" ]
        let each (cls: string) = elements |> List.map (fun e -> e + "." + cls)
        [ sty (each "text-center") [ Style.textAlign TextAlign.Center ]
          sty (each "text-left") [ Style.textAlign TextAlign.Left ]
          sty (each "text-right") [ Style.textAlign TextAlign.Right ]
          sty [ "span.text-center" ] [ Style.display Display.Block; Style.width (Pct 100.0); Style.textAlign TextAlign.Center ]
          sty [ "img.float-left" ] [ Style.float' Float.Left; Style.textAlign TextAlign.Left ]
          sty [ "img.float-right" ] [ Style.float' Float.Right; Style.textAlign TextAlign.Right ]
          sty [ "img.float-center"; "img.text-center" ] [ Style.margin4 Zero Auto Zero Auto; Style.raw "Margin" "0 auto"; Style.float' Float.NoFloat; Style.textAlign TextAlign.Center ]
          sty [ "table.float-center"; "td.float-center"; "th.float-center" ] [ Style.margin4 Zero Auto Zero Auto; Style.raw "Margin" "0 auto"; Style.float' Float.NoFloat; Style.textAlign TextAlign.Center ] ]

    /// The whole stylesheet of Foundation for Emails for these settings.
    let stylesheet (s: Settings) : Rule list =
        [ Comment "Foundation for Emails (ZURB, MIT), written as F#: https://github.com/foundation/foundation-emails" ]
        @ normalize
        @ typography s
        @ gridRules s
        @ [ small s ]
        @ buttonRules s
        @ calloutRules s
        @ alignment

    // ───────────── the markup ─────────────
    let private table (attrs: (string * string) list) (rows: Node list) : Node =
        // role="presentation": these tables are layout, not data (Foundation's markup leaves it out; a screen reader would announce a table)
        Html.el "table" ([ "role", "presentation"; "border", "0"; "cellpadding", "0"; "cellspacing", "0" ] @ attrs) [ Html.el "tbody" [] rows ]
    let private tr (cells: Node list) : Node = Html.el "tr" [] cells
    let private cell (tag: string) (attrs: (string * string) list) (kids: Node list) : Node = Html.el tag attrs kids

    /// `table.container`: the centred column of the email.
    let container (kids: Node list) : Node =
        table [ "align", "center"; Html.cls "container" ] [ tr [ cell "td" [] kids ] ]

    /// `table.row`: a set of columns; `collapse` removes the gutters.
    let row (collapse: bool) (columns: Node list) : Node =
        table [ Html.cls (if collapse then "row collapse" else "row") ] [ tr columns ]

    /// One column: `small` and `large` are of twelve; `first` and `last` take the outer gutters.  Its content sits in a nested table
    /// with an expander cell, as Foundation's Inky compiles it.
    let columnWith (small: int) (large: int) (first: bool) (last: bool) (extra: (string * string) list) (kids: Node list) : Node =
        let classes = "small-" + string small + " large-" + string large + " columns" + (if first then " first" else "") + (if last then " last" else "")
        cell "th" ([ Html.cls classes ] @ extra) [ table [] [ tr [ cell "th" [] kids; cell "th" [ Html.cls "expander" ] [] ] ] ]

    /// A row of columns given as (small, large, content); first and last are set for you.
    let grid12 (collapse: bool) (columns: (int * int * Node list) list) : Node =
        let n = columns.Length
        row collapse (columns |> List.mapi (fun i (sm, lg, kids) -> columnWith sm lg (i = 0) (i = n - 1) [] kids))

    let spacer (height: float) : Node =
        let h = Style.lengthCss (Px height)
        table [ Html.cls "spacer" ] [ tr [ Html.styled "td" [ "height", h ] [ Style.fontSize (Px height); Style.lineHeightLen (Px height) ] [ Html.raw "&#xA0;" ] ] ]

    /// `Outline` is not Foundation's: a transparent button with a border in the primary colour (the rules are in `buttonRules`).
    type ButtonVariant = Primary | Secondary | Success | Warning | Alert | Outline
    type ButtonSize = Tiny | Small | Default | Large

    let button (variant: ButtonVariant) (size: ButtonSize) (expanded: bool) (href: string) (label: Node list) : Node =
        let v = match variant with Primary -> "" | Secondary -> " secondary" | Success -> " success" | Warning -> " warning" | Alert -> " alert" | Outline -> " outline"
        let z = match size with Tiny -> " tiny" | Small -> " small" | Default -> "" | Large -> " large"
        let anchor = Html.el "a" [ "href", href ] label
        let inner = table [] [ tr [ cell "td" [] [ anchor ] ] ]
        table [ Html.cls ("button" + z + v + (if expanded then " expanded" else "")) ] [ tr [ cell "td" [] [ inner ] ] ]

    let callout (tone: string) (kids: Node list) : Node =
        table [ Html.cls "callout" ] [ tr [ cell "th" [ Html.cls (if tone = "" then "callout-inner" else "callout-inner " + tone) ] kids; cell "th" [ Html.cls "expander" ] [] ] ]

    /// The outer table of an email (`table.body`) with the centred cell that the clients need.
    let bodyTable (kids: Node list) : Node =
        table [ Html.cls "body" ] [ tr [ cell "td" [ Html.cls "float-center"; "align", "center"; "valign", "top" ] [ Html.el "center" [] kids ] ] ]

    /// A whole email: the stylesheet is inlined into the elements; what cannot be inlined (the small-screen rules, `:hover`) goes to a
    /// `<style>` block.  `preheader` is the hidden text after the subject.
    let page (s: Settings) (lang: string) (title: string) (preheader: string) (extra: Rule list) (headExtra: string) (bodyExtra: string) (body: Node list) : string =
        let hidden =
            if preheader.Trim() = "" then []
            else [ Html.styled "div" [] [ Style.raw "display" "none"; Style.raw "max-height" "0"; Style.raw "overflow" "hidden"; Style.raw "mso-hide" "all"; Style.raw "font-size" "1px"; Style.raw "line-height" "1px"; Style.raw "opacity" "0" ]
                                [ Html.text preheader; Html.raw (String.replicate 40 "&zwnj;&nbsp;") ] ]
        let tree = bodyTable (hidden @ body)
        let inlined, kept = Inline.apply (stylesheet s @ extra) tree
        let sb = StringBuilder()
        sb.Append("<!DOCTYPE html>\n<html lang=\"").Append(Html.escape lang).Append("\" xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:v=\"urn:schemas-microsoft-com:vml\" xmlns:o=\"urn:schemas-microsoft-com:office:office\">\n<head>\n") |> ignore
        sb.Append("<meta charset=\"utf-8\">\n<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n<meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\">\n") |> ignore
        sb.Append("<meta name=\"x-apple-disable-message-reformatting\">\n<meta name=\"format-detection\" content=\"telephone=no, date=no, address=no, email=no\">\n") |> ignore
        sb.Append("<title>").Append(Html.escape title).Append("</title>\n") |> ignore
        sb.Append("<!--[if mso]><noscript><xml><o:OfficeDocumentSettings><o:PixelsPerInch>96</o:PixelsPerInch></o:OfficeDocumentSettings></xml></noscript><![endif]-->\n") |> ignore
        sb.Append("<style>\n").Append(Style.render Email kept).Append("</style>\n").Append(headExtra).Append("</head>\n") |> ignore
        sb.Append("<body style=\"margin: 0; padding: 0; background: ").Append(Color.css Email s.BodyBackground).Append("\">\n") |> ignore
        sb.Append(Html.render Email inlined).Append("\n").Append(bodyExtra).Append("</body>\n</html>\n") |> ignore
        sb.ToString()
