/// The tests of FSharp.Wasm.Ui: typed CSS, selectors, the inliner, colour and Foundation for Emails' grid.  Run under .NET and as WasmGC.
module Newsletter.UiTests

open Wasmecto
open FSharp.Wasm.Ui

let private has (haystack: string) (needle: string) = Expect.stringContains haystack needle ("contains " + needle)
let private hasNot (haystack: string) (needle: string) = Expect.isFalse (haystack.Contains needle) ("does not contain " + needle)

let color =
    testList "Ui.Color" [
        testCase "hex, rgb, hsl and oklch agree" <| fun () ->
            Expect.equal (Color.toHex (Rgb(13, 110, 253))) (Some "#0d6efd") "rgb"
            Expect.equal (Color.toHex (Hsl(0.0, 0.0, 1.0))) (Some "#ffffff") "hsl white"
            Expect.equal (Color.toHex (Hsl(120.0, 1.0, 0.25))) (Some "#008000") "hsl green"
            Expect.equal (Color.toHex (Oklch(1.0, 0.0, 0.0))) (Some "#ffffff") "oklch white"
            Expect.equal (Color.toHex (Oklch(0.0, 0.0, 0.0))) (Some "#000000") "oklch black"
            // the oklch of sRGB red, as CSS Color 4 gives it
            let r, g, b = Color.ofOklch (0.62796, 0.25768, 29.23)
            Expect.isTrue (r >= 254 && g <= 2 && b <= 2) (sprintf "red: %d %d %d" r g b)
        testCase "an email gets hex, the web gets what was written" <| fun () ->
            Expect.equal (Color.css Email (Oklch(0.6, 0.15, 250.0))).[0] '#' "lowered"
            has (Color.css Web (Oklch(0.6, 0.15, 250.0))) "oklch(0.6 0.15 250)"
            Expect.equal (Color.css Email (Var("brand", Some(Hex "#0d6efd")))) "#0d6efd" "a custom property takes its fallback in an email"
            Expect.equal (Color.css Web (Var("brand", Some(Hex "#0D6EFD")))) "var(--brand, #0d6efd)" "and stays a custom property on the web"
        testCase "contrast follows WCAG" <| fun () ->
            Expect.approxEqual (Color.contrast (Hex "#000") (Hex "#fff") |> Option.get) 21.0 0.01 "black on white"
            Expect.isTrue (Color.meetsAA (Hex "#767676") (Hex "#fff")) "the lightest grey that passes"
            Expect.isFalse (Color.meetsAA (Hex "#777") (Hex "#fff")) "one step lighter does not"
            Expect.isTrue (Color.meetsAA (Oklch(0.3, 0.02, 250.0)) (Oklch(0.98, 0.01, 250.0))) "judged in sRGB, whatever it is written in"
        testCase "Sass' color.adjust and color.scale in the hsl space" <| fun () ->
            Expect.equal (Color.adjustLightness -0.1 (Hex "#2199e8")) (Hex "#147dc2") "the hover of Foundation's primary"
            // Foundation's compiled CSS has rgb(221.7, 239.7, 251.55) and rgb(15.3, 95.1, 147.7) for the primary callout
            Expect.equal (Color.toHex (Color.scaleLightness 0.85 (Hex "#2199e8"))) (Some "#def0fc") "the callout background"
            Expect.equal (Color.toHex (Color.adjustLightness -0.2 (Hex "#2199e8"))) (Some "#0f5f94") "the callout border"
    ]

let css =
    testList "Ui.Css" [
        testCase "numbers are written the way CSS does" <| fun () ->
            Expect.equal (Style.lengthCss (Px 16.0)) "16px" "whole"
            Expect.equal (Style.lengthCss (Px 8.33333333)) "8.3333px" "four decimals"
            Expect.equal (Style.lengthCss (Pct 100.0)) "100%" "percent"
            Expect.equal (Style.lengthCss Zero) "0" "zero"
            Expect.equal (Style.lengthCss (Px -2.5)) "-2.5px" "negative"
        testCase "typed constructors render declarations" <| fun () ->
            Expect.equal (Style.declCss Web (Style.padding (Px 16.0))) "padding: 16px" "length"
            Expect.equal (Style.declCss Web (Style.paddingXY (Px 8.0) (Px 16.0))) "padding: 8px 16px" "pair"
            Expect.equal (Style.declCss Web (Style.border (Px 2.0) BorderStyle.Solid (Hex "#0D6EFD"))) "border: 2px solid #0d6efd" "border"
            Expect.equal (Style.declCss Email (Style.color (Oklch(1.0, 0.0, 0.0)))) "color: #ffffff" "lowered for an email"
            Expect.equal (Style.declCss Web (Style.important (Style.display Display.NoDisplay))) "display: none !important" "important"
            Expect.equal (Style.declCss Web (Style.textAlign TextAlign.Center)) "text-align: center" "keyword"
        testCase "merging: a later value wins, an earlier !important stays" <| fun () ->
            let merged = Style.merge [ Style.padding (Px 1.0); Style.color (Hex "#000"); Style.padding (Px 2.0) ]
            Expect.equal (merged |> List.map (Style.declCss Web)) [ "color: #000"; "padding: 2px" ] "later wins"
            let kept = Style.merge [ Style.important (Style.padding (Px 1.0)); Style.padding (Px 2.0) ]
            Expect.equal (kept |> List.map (Style.declCss Web)) [ "padding: 1px !important" ] "important stays"
        testCase "selectors parse, print and have a specificity" <| fun () ->
            let roundTrip (text: string) = Selector.css (Selector.parse text)
            Expect.equal (roundTrip "table.container") "table.container" "tag and class"
            Expect.equal (roundTrip ".collapse > tbody > tr > td.large-3:not([class*=large-offset])") ".collapse > tbody > tr > td.large-3:not([class*=\"large-offset\"])" "child chain, :not, attribute"
            Expect.equal (roundTrip "a:hover") "a:hover" "pseudo-class"
            Expect.equal (roundTrip "[data-selected]") "[data-selected]" "attribute only"
            Expect.equal (Selector.specificity (Selector.parse "td.columns.first")) 201 "one tag, two classes"
            Expect.equal (Selector.specificity (Selector.parse "#a .b p")) 10101 "id, class, tag"
            Expect.isFalse (Selector.isInlinable (Selector.parse "table.button:hover table td")) "state cannot be inlined"
        testCase "a stylesheet renders as nested text" <| fun () ->
            let text =
                Style.render Web
                    [ Style.rule [ "a"; ".b > c" ] [ Style.color (Hex "#fff"); Style.important (Style.margin Zero) ]
                      Style.mediaMaxWidth (Px 600.0) [ Style.rule [ ".x" ] [ Style.width (Pct 100.0) ] ]
                      Layer("base", [ Style.rule [ "p" ] [ Style.lineHeight 1.5 ] ]) ]
            Expect.equal text "a,\n.b > c {\n  color: #fff;\n  margin: 0 !important;\n}\n@media only screen and (max-width: 600px) {\n  .x {\n    width: 100%;\n  }\n}\n@layer base {\n  p {\n    line-height: 1.5;\n  }\n}\n" "text"
    ]

let inlining =
    let td = Html.el "td" [ Html.cls "columns first" ] [ Html.text "x" ]
    testList "Ui.Inline" [
        testCase "rules are applied by specificity, then order; the element's own style comes last" <| fun () ->
            let rules =
                [ Style.rule [ "td" ] [ Style.color (Hex "#111"); Style.padding (Px 1.0) ]
                  Style.rule [ "td.columns" ] [ Style.padding (Px 2.0) ]
                  Style.rule [ "td" ] [ Style.color (Hex "#222") ]
                  Style.rule [ ".nothing" ] [ Style.color (Hex "#f00") ] ]
            let inlined, kept = Inline.apply rules (Html.styled "td" [ Html.cls "columns" ] [ Style.margin Zero ] [])
            Expect.equal (Html.render Web inlined) "<td class=\"columns\" style=\"color: #222; padding: 2px; margin: 0\"></td>" "merged"
            Expect.equal kept [] "nothing left over"
        testCase "descendant and child selectors look at the ancestors" <| fun () ->
            let rules = [ Style.rule [ "table.row th" ] [ Style.color (Hex "#111") ]; Style.rule [ "table > tbody > tr > th" ] [ Style.padding (Px 3.0) ]; Style.rule [ "div th" ] [ Style.color (Hex "#f00") ] ]
            let tree = Html.el "table" [ Html.cls "row" ] [ Html.el "tbody" [] [ Html.el "tr" [] [ Html.el "th" [] [] ] ] ]
            let inlined, _ = Inline.apply rules tree
            has (Html.render Web inlined) "<th style=\"padding: 3px; color: #111\"></th>"
        testCase ":not and attribute selectors" <| fun () ->
            let rules = [ Style.rule [ "td:not([class*=offset])" ] [ Style.padding Zero ] ]
            let inlined, _ = Inline.apply rules (Html.el "tr" [] [ td; Html.el "td" [ Html.cls "large-offset-1" ] [] ])
            let out = Html.render Web inlined
            has out "<td class=\"columns first\" style=\"padding: 0\">"
            has out "<td class=\"large-offset-1\"></td>"
        testCase "what cannot be inlined stays in the stylesheet" <| fun () ->
            let rules = [ Style.rule [ "a"; "a:hover" ] [ Style.color (Hex "#111") ]; Style.mediaMaxWidth (Px 600.0) [ Style.rule [ "a" ] [ Style.color (Hex "#222") ] ] ]
            let inlined, kept = Inline.apply rules (Html.el "a" [] [])
            has (Html.render Web inlined) "style=\"color: #111\""
            Expect.equal kept.Length 2 "the media query and the :hover rule"
        testCase "rendering escapes text and attributes" <| fun () ->
            let out = Html.render Web (Html.el "p" [ "title", "a\"b" ] [ Html.text "<b>&" ])
            Expect.equal out "<p title=\"a&quot;b\">&lt;b&gt;&amp;</p>" "escaped"
            Expect.equal (Html.render Web (Html.el "img" [ "src", "x" ] [])) "<img src=\"x\">" "void elements have no end tag"
    ]

let ink =
    testList "Ui.Ink (Foundation for Emails)" [
        testCase "the stylesheet has the numbers of Foundation's compiled CSS" <| fun () ->
            let text = Style.render Email (Ink.stylesheet Ink.defaults)
            has text "th.large-6 {\n  width: 274px;\n  padding-left: 8px;\n  padding-right: 8px;\n}"
            has text "td.large-12.first,\n"                         // 12 columns of 580 px
            has text "width: 564px"                                  // large-12: 580 - 16
            has text "@media only screen and (max-width: 596px)"     // the breakpoint: width + gutter
            has text "width: 8.3333% !important"                     // small-1
            has text "visibility: hidden"                            // the expander
        testCase "one column is a row of one: nested table with an expander" <| fun () ->
            let out = Html.render Email (Ink.grid12 false [ (12, 12, [ Html.el "p" [] [ Html.text "x" ] ]) ])
            has out "<th class=\"small-12 large-12 columns first last\">"
            has out "<th class=\"expander\"></th>"
            has out "role=\"presentation\""
        testCase "a whole page inlines the stylesheet and keeps the small-screen rules" <| fun () ->
            let page = Ink.page Ink.defaults "en" "T" "pre" [] "" "" [ Ink.container [ Ink.grid12 false [ (12, 6, []); (12, 6, []) ] ] ]
            has page "class=\"container\""
            has page "width: 580px"
            has page "@media only screen and (max-width: 596px)"
            hasNot page "table.container {"                          // inlined, not left as a rule
        testCase "the same page for another width moves the breakpoint and the columns" <| fun () ->
            let wide = { Ink.defaults with GlobalWidth = 640.0 }
            let page = Ink.page wide "en" "T" "" [] "" "" [ Ink.container [ Ink.grid12 false [ (12, 6, []); (12, 6, []) ] ] ]
            has page "max-width: 656px"
            has page "width: 304px"                                  // 640 / 12 * 6 - 16
    ]

let suites : Test list = [ color; css; inlining; ink ]
