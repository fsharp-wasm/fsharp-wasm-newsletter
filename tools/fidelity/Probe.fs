// Renders one page with the grid, columns, buttons, a callout and a spacer twice: the markup alone (raw.html) and inlined by our
// Inline.apply with Ink.stylesheet (ours-inlined.html).  compare.sh inlines raw.html with juice and Foundation's compiled CSS.
open FSharp.Wasm.Ui

[<EntryPoint>]
let main argv =
    let out = if argv.Length > 0 then argv.[0] else "."
    let s = Ink.defaults
    let p (t: string) = Html.el "p" [] [ Html.text t ]
    let body =
        [ Ink.container
            [ Ink.grid12 false [ (12, 6, [ Html.el "h1" [] [ Html.text "Title" ]; p "One" ]); (12, 6, [ p "Two" ]) ]
              Ink.grid12 false [ (12, 12, [ Html.el "p" [ "class", "lead" ] [ Html.text "Wide" ] ]) ]
              Ink.grid12 true [ (6, 4, [ p "a" ]); (6, 4, [ p "b" ]); (12, 4, [ p "c" ]) ]
              Ink.spacer 16.0
              Ink.button Ink.Primary Ink.Default false "https://example.com" [ Html.text "Go" ]
              Ink.button Ink.Secondary Ink.Large true "https://example.com" [ Html.text "Big" ]
              Ink.callout "primary" [ p "Note" ]
              Html.el "p" [ "class", "text-center" ] [ Html.el "a" [ "href", "https://example.com" ] [ Html.text "link" ]; Html.el "small" [] [ Html.text " small" ] ] ] ]
    let tree = Ink.bodyTable body
    System.IO.File.WriteAllText(out + "/raw.html", Html.render Email tree)
    let inlined, kept = Inline.apply (Ink.stylesheet s) tree
    System.IO.File.WriteAllText(out + "/ours-inlined.html", Html.render Email inlined)
    printfn "inlined; %d rules stay in <style>" kept.Length
    0
