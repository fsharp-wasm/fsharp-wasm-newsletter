/// The tests of the newsletter library.  They are written with Wasmecto and run twice: under .NET (`dotnet run`) and inside a
/// WebAssembly GC engine (`bash build/test.sh`) — the designer ships as WasmGC, so that is the run that counts.
module Newsletter.Suites

open Wasmecto
open Newsletter
open FSharp.Wasm.Data
open Newsletter.Model
open Newsletter.Profile

let private has (haystack: string) (needle: string) = Expect.stringContains haystack needle ("contains " + needle)
let private hasNot (haystack: string) (needle: string) = Expect.isFalse (haystack.Contains needle) ("does not contain " + needle)

/// How often a text occurs in another (no String.Split: the suites run as WasmGC too).
let private occurrences (needle: string) (text: string) : int =
    let mutable n = 0
    for i in 0 .. text.Length - needle.Length do
        if text.Substring(i, needle.Length) = needle then n <- n + 1
    n
let private count (haystack: string) (needle: string) : int = (haystack.Split([| needle |], System.StringSplitOptions.None)).Length - 1

let private blockOf (kind: Kind) (tweak: Block -> Block) : Document =
    let d, id = addBlock kind None emptyDocument
    updateBlock id tweak d

let private email (d: Document) = Render.email Render.exportOptions defaultProfile d
let private web (p: Profile) (d: Document) = Render.web Render.exportOptions p d

// ───────────────────────────── model ─────────────────────────────
let model =
    testList "Model" [
        testCase "parseInt" <| fun () ->
            Expect.equal (parseInt " 42 ") (Some 42) "trims"
            Expect.equal (parseInt "-7") (Some -7) "negative"
            Expect.equal (parseInt "4x") None "garbage"
            Expect.equal (parseInt "") None "empty"
            Expect.equal (parseInt "99999999999") None "too long is not a number here"
        testCase "add after the selection, else at the end" <| fun () ->
            let d1, a = addBlock Heading None emptyDocument
            let d2, b = addBlock Paragraph None d1
            let d3, c = addBlock Divider (Some a) d2
            Expect.equal (d3.Blocks |> List.map (fun x -> x.Id)) [ a; c; b ] "after a"
            Expect.equal d3.NextId 4 "ids keep counting"
        testCase "move stops at the ends" <| fun () ->
            let d1, a = addBlock Heading None emptyDocument
            let d2, b = addBlock Paragraph None d1
            Expect.equal ((moveBlock b -1 d2).Blocks |> List.map (fun x -> x.Id)) [ b; a ] "up"
            Expect.equal (moveBlock a -1 d2) d2 "first stays"
            Expect.equal (moveBlock b 5 d2) d2 "last stays"
        testCase "duplicate and remove" <| fun () ->
            let d1, a = addBlock Button None emptyDocument
            let d2, copy = duplicateBlock a d1
            Expect.equal d2.Blocks.Length 2 "two"
            Expect.equal (removeBlock a d2).Blocks.Length 1 "one left"
            Expect.isSome copy "has the id of the copy"
        testCase "numbers are clamped by kind" <| fun () ->
            let h = blank Heading 1
            Expect.equal (setField FLevel "9" h).Level 3 "level max"
            Expect.equal (setField FLevel "0" h).Level 1 "level min"
            Expect.equal (setField FLevel "x" h) h "unparsable keeps the block"
            Expect.equal (setField FSize "500" (blank Spacer 1)).Size 120 "spacer max"
            Expect.equal (setField FSize "1" (blank Image 1)).Size 10 "image min"
        testCase "every kind offers fields that exist" <| fun () ->
            for (kind, _, _) in kinds do
                let b = blank kind 1
                for spec in fieldsOf kind do
                    Expect.equal (getField spec.Field (setField spec.Field (getField spec.Field b) b)) (getField spec.Field b) ("stable " + kindKey kind)
        testCase "the summary says what a block holds, in one short line" <| fun () ->
            Expect.equal (summary (blank Spacer 1)) "24 px" "spacer"
            Expect.equal (summary { blank Image 1 with Alt = "" }) "no alternative text" "image"
            Expect.equal (summary { blank Heading 1 with Text = "Two\nlines" }) "Two lines" "one line"
            Expect.isTrue ((summary { blank Heading 1 with Text = String.replicate 100 "x" }).Length <= 44) "clipped"
        testCase "kinds have unique keys" <| fun () ->
            let keys = kinds |> List.map (fun (_, k, _) -> k)
            Expect.equal (List.distinct keys).Length keys.Length "unique"
            Expect.equal (kindOfKey "columns") (Some Columns) "lookup"
            Expect.equal (kindOfKey "nope") None "unknown"
    ]

// ───────────────────────────── colour ─────────────────────────────
let color =
    testList "Color" [
        testCase "parse" <| fun () ->
            Expect.equal (Color.parse "#fff") (Some(255, 255, 255)) "short"
            Expect.equal (Color.parse "#0d6efd") (Some(13, 110, 253)) "long"
            Expect.equal (Color.parse "red") None "names are not colours here"
            Expect.equal (Color.parse "#12345") None "bad length"
            Expect.equal (Color.parse "#12g456") None "bad digit"
        testCase "contrast" <| fun () ->
            Expect.approxEqual (Color.contrast "#000000" "#ffffff" |> Option.get) 21.0 0.01 "black on white"
            Expect.approxEqual (Color.contrast "#777777" "#ffffff" |> Option.get) 4.48 0.01 "the well-known grey"
            Expect.isTrue (Color.meetsAA "#767676" "#ffffff") "#767676 is the lightest grey that passes"
            Expect.isFalse (Color.meetsAA "#777777" "#ffffff") "#777777 does not"
        testCase "the shipped profiles meet AA for their buttons and text" <| fun () ->
            for p in [ bootstrap; tailwind ] do
                Expect.isTrue (Color.meetsAA (token "onPrimary" p) (token "primary" p)) (p.Name + ": primary button")
                Expect.isTrue (Color.meetsAA (token "onSecondary" p) (token "secondary" p)) (p.Name + ": secondary button")
                Expect.isTrue (Color.meetsAA (token "text" p) (token "surface" p)) (p.Name + ": text")
                Expect.isTrue (Color.meetsAA (token "muted" p) (token "surface" p)) (p.Name + ": muted text")
    ]

// ───────────────────────────── markup ─────────────────────────────
let markup =
    testList "Markup" [
        testCase "escape" <| fun () ->
            Expect.equal (Markup.escape "<a href=\"x\">&'") "&lt;a href=&quot;x&quot;&gt;&amp;&#39;" "all five"
        testCase "safeUrl lets links through and stops code" <| fun () ->
            for ok in [ "https://example.com/a?b=c"; "http://x.y"; "mailto:a@b.c"; "tel:+49123"; "/path"; "#top"; "relative/page"; "{{unsubscribe_url}}"; "https://a.b/x:y" ] do
                Expect.equal (Markup.safeUrl ok) ok ("kept: " + ok)
            for bad in [ "javascript:alert(1)"; "JaVaScRiPt:alert(1)"; "data:text/html,<script>"; "vbscript:x"; "  javascript:x"; "" ] do
                Expect.equal (Markup.safeUrl bad) "#" ("blocked: " + bad)
        testCase "paragraphs" <| fun () ->
            Expect.equal (Markup.paragraphs "a\n\nb\r\n\r\n\n  c  ") [ "a"; "b"; "c" ] "split at blank lines"
            Expect.equal (Markup.paragraphs "  ") [] "blank is nothing"
        testCase "inline markup" <| fun () ->
            Expect.equal (Markup.inlineHtml "" "a **b** and _c_ and [d](https://e.f)") "a <strong>b</strong> and <em>c</em> and <a href=\"https://e.f\">d</a>" "all three"
            Expect.equal (Markup.inlineHtml "" "snake_case_name 2*3*4") "snake_case_name 2*3*4" "no accidental emphasis"
            Expect.equal (Markup.inlineHtml "" "<b>x</b>") "&lt;b&gt;x&lt;/b&gt;" "html is text"
            Expect.equal (Markup.inlineHtml "" "[x](javascript:alert(1))") "<a href=\"#\">x</a>" "a bad link keeps its text, loses its target"
            Expect.equal (Markup.inlineHtml "style=\"s\"" "[x](/y)") "<a href=\"/y\" style=\"s\">x</a>" "link attributes"
        testCase "text form" <| fun () ->
            Expect.equal (Markup.inlineText "**b** [l](https://x.y)") "b l (https://x.y)" "links keep their address"
    ]

// ───────────────────────────── renderers ─────────────────────────────
let render =
    testList "Render" [
        testCase "the email is Foundation's table grid with every style inlined" <| fun () ->
            let h = email Sample.issue
            has h "<!DOCTYPE html>"
            has h "<html lang=\"en\""
            has h "<table role=\"presentation\""
            has h "<!--[if mso]>"
            has h "class=\"container\""
            has h "class=\"small-12 large-6 columns first\""
            has h "class=\"small-12 large-6 columns last\""
            has h "class=\"expander\""
            has h "font-weight: bold"                                   // headings: the newsletter's own rule on top of Foundation's
            has h "@media only screen and (max-width: 616px)"
            hasNot h "<link"
            hasNot h "<script"
            hasNot h "data-block"
            Expect.equal (count h "<h1") 1 "one h1"
        testCase "the grid is Foundation's: a column is a twelfth of the width minus the gutter" <| fun () ->
            let narrow = Render.email Render.exportOptions (bootstrap |> setToken "width" "580") Sample.issue
            has narrow "width: 274px"                                  // large-6: 580 / 12 * 6 - 16
            has narrow "width: 580px"                                  // the container
        testCase "the preheader is hidden and the subject is the title" <| fun () ->
            let h = email Sample.issue
            has h "display: none; max-height: 0; overflow: hidden; mso-hide: all"
            has h "Three short stories about programs that stay out of the way."
            has h "<title>Small software, big difference</title>"
        testCase "a button is a table: the label is a link, the colours come from the profile" <| fun () ->
            let h = email (blockOf Button (fun b -> { b with Text = "Go"; Url = "https://x.y"; Variant = Primary }))
            has h "class=\"button"
            has h "href=\"https://x.y\""
            has h "background: #0d6efd"
            has h "color: #ffffff"
            let o = email (blockOf Button (fun b -> { b with Text = "Go"; Url = "https://x.y"; Variant = Outline }))
            has o "class=\"button outline"
            has o "background: transparent"
            let centred = email (blockOf Button (fun b -> { b with Text = "Go"; Url = "https://x.y"; Align = Center }))
            has centred "float-center"
        testCase "text from the author cannot become markup" <| fun () ->
            let d = blockOf Paragraph (fun b -> { b with Text = "<script>alert(1)</script> & \"quotes\"" })
            let h = email d
            hasNot h "<script>alert"
            has h "&lt;script&gt;"
            let img = email (blockOf Image (fun b -> { b with Url = "javascript:alert(1)"; Alt = "\"><script>x</script>" }))
            hasNot img "javascript:"
            hasNot img "<script>x"
        testCase "an imported profile cannot inject through a token" <| fun () ->
            let evil = bootstrap |> setToken "primary" "red;\"><script>x</script>" |> setToken "font" "Arial\"><script>y</script>"
            let h = Render.email Render.exportOptions evil Sample.issue
            hasNot h "<script>x"
            hasNot h "<script>y"
            has h "#0d6efd"
        testCase "width and radius follow the profile and stay in range" <| fun () ->
            let p = bootstrap |> setToken "width" "700" |> setToken "radius" "0"
            let h = Render.email Render.exportOptions p Sample.issue
            has h "width: 700px"
            let wild = Render.email Render.exportOptions (bootstrap |> setToken "width" "99999") Sample.issue
            has wild "width: 900px"
        testCase "the preview marks blocks and highlights the selection; the export does not" <| fun () ->
            let d = Sample.issue
            let o : Render.Options = { Preview = true; Selected = 2; Local = id }
            let h = Render.email o defaultProfile d
            has h "data-block=\"1\""
            has h "data-block=\"2\" data-selected"
            has h "postMessage"
            hasNot (Render.web Render.exportOptions defaultProfile d) "postMessage"
        testCase "the web version uses the framework's own class names and stylesheet" <| fun () ->
            let h = web bootstrap Sample.issue
            has h "<link rel=\"stylesheet\" href=\"https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css\">"
            has h "class=\"btn btn-primary\""
            has h "class=\"row g-4\""
            has h "<main class=\"container py-4\">"
            has h "<h1 class=\"display-6 fw-bold mb-3 text-start\">"
            has h "<footer"
        testCase "another profile changes the markup, not the document" <| fun () ->
            let h = web tailwind Sample.issue
            has h "<script src=\"https://cdn.tailwindcss.com/3.4.16\"></script>"
            has h "bg-blue-600"
            hasNot h "btn-primary"
        testCase "a custom profile with no classes renders plain, valid HTML" <| fun () ->
            let h = web custom Sample.issue
            hasNot h "class="
            has h "<main>"
            hasNot h "<link"
        testCase "the web version keeps headings, alt text and landmarks" <| fun () ->
            let h = web bootstrap Sample.issue
            has h "alt=\"The Weekly — a newsletter about small, fast software\""
            has h "<html lang=\"en\">"
            has h "<main"
        testCase "the plain-text alternative" <| fun () ->
            let t = Render.text Sample.issue
            has t "SMALL SOFTWARE, BIG DIFFERENCE\n=============================="
            has t "Read the main story: https://example.com/story"
            has t "How we measured (https://example.com/measure)"
            has t "> Simplicity is a prerequisite for reliability.\n> — Edsger W. Dijkstra"
            has t "Unsubscribe: {{unsubscribe_url}}"
            hasNot t "**"
            hasNot t "<"
    ]

// ───────────────────────────── JSON codec ─────────────────────────────
let codec =
    testList "Codec" [
        testCase "a project round-trips" <| fun () ->
            let text = Codec.projectToJson Sample.issue bootstrap
            match Codec.projectOfJson text with
            | Ok(d, p) ->
                Expect.equal d.Blocks (Sample.issue.Blocks |> List.mapi (fun i b -> { b with Id = i + 1 })) "blocks"
                Expect.equal d.Subject Sample.issue.Subject "subject"
                Expect.equal p bootstrap "profile"
            | Error e -> Expect.isTrue false e
        testCase "the file names only the fields a block uses" <| fun () ->
            let text = Codec.projectToJson (blockOf Divider id) bootstrap
            has text "\"type\": \"divider\""
            hasNot text "\"alt\""
        testCase "unknown blocks are skipped and missing fields fall back" <| fun () ->
            let text = "{\"document\":{\"subject\":\"S\",\"blocks\":[{\"type\":\"hologram\"},{\"type\":\"heading\"},{\"type\":\"spacer\",\"size\":9999}]}}"
            match Codec.projectOfJson text with
            | Ok(d, p) ->
                Expect.equal d.Blocks.Length 2 "the unknown one is dropped"
                Expect.equal d.Blocks.[0].Text (blank Heading 1).Text "default text"
                Expect.equal d.Blocks.[1].Size 120 "clamped"
                Expect.equal p defaultProfile "default profile"
            | Error e -> Expect.isTrue false e
        testCase "bad input is an error, not a crash" <| fun () ->
            Expect.isTrue (match Codec.projectOfJson "nope" with Error _ -> true | Ok _ -> false) "not json"
            Expect.isTrue (match Codec.projectOfJson "{}" with Error _ -> true | Ok _ -> false) "no document"
            Expect.isTrue (match Codec.profileOfJson "[1]" with Error _ -> true | Ok _ -> false) "profile must be an object"
        testCase "a profile round-trips on its own" <| fun () ->
            let p = tailwind |> setToken "primary" "#123456"
            Expect.equal (Codec.profileOfJson (Json.pretty (Profile.toJson p))) (Ok p) "same"
    ]

// ───────────────────────────── checks ─────────────────────────────
let lint =
    let run (d: Document) = Lint.check defaultProfile d (email d).Length
    let messages (fs: Lint.Finding list) = fs |> List.map (fun f -> f.Message)
    testList "Lint" [
        testCase "the sample issue has no errors" <| fun () ->
            Expect.equal (Lint.errors (run Sample.issue)) 0 (String.concat "; " (messages (run Sample.issue)))
        testCase "an empty document asks for what is missing" <| fun () ->
            let ms = messages (run emptyDocument)
            Expect.isTrue (ms |> List.exists (fun m -> m.Contains "subject")) "subject"
            Expect.isTrue (ms |> List.exists (fun m -> m.Contains "footer")) "footer"
        testCase "an image needs alternative text" <| fun () ->
            let d = blockOf Image (fun b -> { b with Alt = "" })
            Expect.isTrue (run d |> List.exists (fun f -> f.Severity = Lint.Error && f.Message.Contains "alternative text")) "error"
        testCase "findings point at their block" <| fun () ->
            let d = blockOf Button (fun b -> { b with Url = "" })
            Expect.isTrue (run d |> List.exists (fun f -> f.Block = Some 1 && f.Message.Contains "without a link")) "block 1"
        testCase "vague labels and low contrast are warned about" <| fun () ->
            let weak = bootstrap |> setToken "primary" "#9ecbff"
            let d = blockOf Button (fun b -> { b with Text = "Click here"; Url = "https://x.y" })
            let fs = Lint.check weak d 1000
            Expect.isTrue (fs |> List.exists (fun f -> f.Message.Contains "does not say where")) "vague"
            Expect.isTrue (fs |> List.exists (fun f -> f.Message.Contains "contrast")) "contrast"
        testCase "heading levels are checked" <| fun () ->
            let d1, _ = addBlock Heading None emptyDocument
            let d2 = d1 |> updateBlock 1 (fun b -> { b with Level = 3 })
            Expect.isTrue (run d2 |> List.exists (fun f -> f.Message.Contains "first heading")) "first is not 1"
            let d3, id = addBlock Heading None (blockOf Heading (fun b -> { b with Level = 1 }))
            let d4 = updateBlock id (fun b -> { b with Level = 3 }) d3
            Expect.isTrue (run d4 |> List.exists (fun f -> f.Message.Contains "skip levels")) "1 then 3"
        testCase "a message over 102 KB is flagged" <| fun () ->
            Expect.isTrue (Lint.check defaultProfile Sample.issue (103 * 1024) |> List.exists (fun f -> f.Message.Contains "Gmail")) "clipped"
    ]

// ───────────────────────────── the designer's state ─────────────────────────────
let studio =
    let send (msgs: Studio.Msg list) (m: Studio.Model) = msgs |> List.fold (fun (m, fx) msg -> let m2, e = Studio.update msg m in m2, fx @ e) (m, [])
    let start = { Studio.init with Doc = emptyDocument }
    // a window 1000 px tall: the margin where a drag scrolls the page is its top and bottom 64 px, and no drag below sits in it
    let tall = 1000.0
    testList "Studio" [
        testCase "adding selects the new block and opens the inspector" <| fun () ->
            let m, fx = send [ Studio.Add Heading; Studio.Add Button ] start
            Expect.equal m.Doc.Blocks.Length 2 "two blocks"
            Expect.equal m.Selected (Some 2) "the last one is selected"
            Expect.equal m.Panel Studio.PBlock "inspector"
            Expect.isTrue (fx |> List.exists (fun e -> match e with Studio.Save _ -> true | _ -> false)) "saved"
        testCase "a new block goes after the selected one" <| fun () ->
            let m, _ = send [ Studio.Add Heading; Studio.Add Paragraph; Studio.Select(Some 1); Studio.Add Divider ] start
            Expect.equal (m.Doc.Blocks |> List.map (fun b -> kindKey b.Kind)) [ "heading"; "divider"; "paragraph" ] "order"
        testCase "undo and redo" <| fun () ->
            let m1, _ = send [ Studio.Add Heading; Studio.Add Button ] start
            let m2, _ = send [ Studio.Undo ] m1
            Expect.equal m2.Doc.Blocks.Length 1 "undone"
            let m3, _ = send [ Studio.Redo ] m2
            Expect.equal m3.Doc.Blocks.Length 2 "redone"
            let m4, _ = send [ Studio.Undo; Studio.Undo; Studio.Undo ] m3
            Expect.equal m4.Doc.Blocks.Length 0 "all the way back"
            Expect.equal m4.Notice "Nothing to undo." "says so"
        testCase "typing into one field is one step of the log" <| fun () ->
            let m1, _ = send [ Studio.Add Heading ] start
            let typed, _ = send [ Studio.EditField(1, FText, "H"); Studio.EditField(1, FText, "He"); Studio.EditField(1, FText, "Hel") ] m1
            Expect.equal typed.Doc.Blocks.[0].Text "Hel" "text"
            let back, _ = send [ Studio.Undo ] typed
            Expect.equal back.Doc.Blocks.[0].Text (blank Heading 1).Text "one undo reverts the whole word"
            let other, _ = send [ Studio.EditField(1, FText, "a"); Studio.EditField(1, FAlign, "center"); Studio.EditField(1, FText, "ab") ] m1
            Expect.equal other.History.Past.Length 4 "a different field starts a new step"
        testCase "a change after undo drops the redo branch" <| fun () ->
            let m, _ = send [ Studio.Add Heading; Studio.Undo; Studio.Add Divider ] start
            Expect.isFalse (Studio.canRedo m) "no redo"
        testCase "moves are announced; the ends are explained" <| fun () ->
            let m, _ = send [ Studio.Add Heading; Studio.Add Paragraph; Studio.Move(2, -1) ] start
            has m.Notice "moved up"
            let m2, fx = send [ Studio.Move(2, -1) ] m
            has m2.Notice "Already first"
            Expect.equal fx [] "nothing changed, nothing saved"
        testCase "removing the selection clears it" <| fun () ->
            let m, _ = send [ Studio.Add Heading; Studio.Remove 1 ] start
            Expect.equal m.Selected None "none"
            Expect.equal m.Doc.Blocks [] "gone"
        testCase "profiles: pick, edit a token, edit a class" <| fun () ->
            let m, _ = send [ Studio.PickProfile "Tailwind CSS 3"; Studio.EditToken("primary", "#111111"); Studio.EditClass("p", "mb-9") ] start
            Expect.equal m.Profile.Name "Tailwind CSS 3" "tailwind"
            Expect.equal (token "primary" m.Profile) "#111111" "token"
            Expect.equal (classes "p" m.Profile) "mb-9" "class"
            let back, _ = send [ Studio.Undo; Studio.Undo; Studio.Undo ] m
            Expect.equal back.Profile bootstrap "undo restores the profile"
        testCase "restoring what the browser kept does not save it again" <| fun () ->
            let json = Codec.projectToJson Sample.issue bootstrap
            let m, fx = send [ Studio.Restore json ] start
            Expect.equal m.Doc.Subject Sample.issue.Subject "loaded"
            Expect.equal fx [] "no save"
            let broken, _ = send [ Studio.Restore "{{{" ] start
            Expect.equal broken.Doc start.Doc "unreadable is ignored"
        testCase "import: a project, a profile, and errors" <| fun () ->
            let project = Codec.projectToJson Sample.issue tailwind
            let m, _ = send [ Studio.ImportChanged project; Studio.ApplyImport ] start
            Expect.equal m.Profile.Name "Tailwind CSS 3" "profile came with it"
            Expect.equal m.Doc.Blocks.Length Sample.issue.Blocks.Length "document came with it"
            let prof = Json.stringify (Profile.toJson (bootstrap |> setToken "primary" "#abcdef"))
            let m2, _ = send [ Studio.ImportChanged prof; Studio.ApplyImport ] start
            Expect.equal (token "primary" m2.Profile) "#abcdef" "profile only"
            Expect.equal m2.Doc start.Doc "document untouched"
            let m3, _ = send [ Studio.ImportChanged "{\"x\":1}"; Studio.ApplyImport ] start
            Expect.isTrue (m3.ImportError <> "") "explains"
            let m4, _ = send [ Studio.ImportChanged "oops"; Studio.ApplyImport ] start
            has m4.ImportError "Not valid JSON"
        testCase "copy asks the browser for exactly the shown output" <| fun () ->
            let m, _ = send [ Studio.SetOutput Studio.OutText ] { start with Doc = Sample.issue }
            let _, fx = send [ Studio.Copy ] m
            Expect.equal fx [ Studio.Clipboard(Render.text Sample.issue) ] "text"
        testCase "the preview shows the selected block and uses the local stylesheet" <| fun () ->
            let m, _ = send [ Studio.Select(Some 2); Studio.SetMedium Studio.Web ] { start with Doc = Sample.issue }
            let h = Studio.previewHtml m
            has h "data-block=\"2\" data-selected"
            has h "href=\"vendor/bootstrap/bootstrap.min.css\""
            hasNot h "cdn.jsdelivr.net"
            let exported, _ = send [ Studio.SetOutput Studio.OutWeb ] m
            has (Studio.output exported) "cdn.jsdelivr.net"
        // ── dragging a block ──
        testCase "dragging a block to another place moves it, in one step of the log" <| fun () ->
            let three, _ = send [ Studio.Add Heading; Studio.Add Paragraph; Studio.Add Divider; Studio.Select None ] start
            let ids = three.Doc.Blocks |> List.map (fun b -> b.Id)
            // the blocks are measured one above the other: 40 px high, 50 px apart, in a column 200 wide
            let slots = ids |> List.mapi (fun i id -> (id, 0.0, float i * 50.0, 200.0, 40.0, false))
            let moved, fx =
                send [ Studio.DragPress(Studio.DragBlock ids.[0], 20.0, 20.0)       // on the first block
                       Studio.DragMove(20.0, 60.0, slots, tall)                            // beyond the threshold, over the second
                       Studio.DragMove(20.0, 128.0, slots, tall)                           // over the lower half of the third
                       Studio.DragRelease(20.0, 128.0, slots) ] three
            Expect.equal (moved.Doc.Blocks |> List.map (fun b -> b.Id)) [ ids.[1]; ids.[2]; ids.[0] ] "the first block is last now"
            has moved.Notice "moved by dragging"
            Expect.isTrue (fx |> List.exists (fun e -> match e with Studio.Save _ -> true | _ -> false)) "saved"
            Expect.equal (moved.History.Past.Length - three.History.Past.Length) 1 "one step"
            let back, _ = send [ Studio.Undo ] moved
            Expect.equal (back.Doc.Blocks |> List.map (fun b -> b.Id)) ids "undo puts it back"
        // ── the page scrolls for a drag near the window's edge ──
        testCase "a drag near the bottom edge asks for one frame; each frame scrolls and asks the next while the pointer rests there" <| fun () ->
            let two, _ = send [ Studio.Add Heading; Studio.Add Paragraph; Studio.Select None ] start
            let ids = two.Doc.Blocks |> List.map (fun b -> b.Id)
            let slots = ids |> List.mapi (fun i id -> (id, 0.0, 900.0 + float i * 50.0, 200.0, 40.0, false))
            let pressed, _ = send [ Studio.DragPress(Studio.DragBlock ids.[0], 20.0, 920.0) ] two
            let middle, fx = send [ Studio.DragMove(20.0, 500.0, slots, tall) ] pressed
            Expect.equal fx [] "in the middle of the window nothing scrolls, and no frame is asked for"
            let near, fx = send [ Studio.DragMove(20.0, 985.0, slots, tall) ] middle
            Expect.equal fx [ Studio.NextFrame ] "49 px into the margin: a frame is asked for"
            let moved, fx = send [ Studio.DragMove(20.0, 990.0, slots, tall) ] near
            Expect.equal fx [] "a pointer that moves on meanwhile does not ask for a second one"
            let frame, fx = send [ Studio.Frame(tall, slots, 1000.0) ] moved
            Expect.equal fx [ Studio.ScrollBy 16.0; Studio.NextFrame ] "the pointer is 54 px into the margin: 54/64 of the fastest 18 px a frame is 15.2, rounded up to whole pixels; and the next frame"
            let again, fx = send [ Studio.Frame(tall, slots, 1000.0 + 1000.0 / 60.0) ] frame
            Expect.equal fx [ Studio.ScrollBy 16.0; Studio.NextFrame ] "and so on while it rests"
            Expect.isTrue (Studio.dragActive again) "the drag goes on"
            Expect.equal again.Doc two.Doc "nothing is changed by scrolling"
        testCase "the page scrolls as fast on a fast screen as on a slow one: by the time since the frame before, and not by more than three frames' worth" <| fun () ->
            let two, _ = send [ Studio.Add Heading; Studio.Add Paragraph; Studio.Select None ] start
            let ids = two.Doc.Blocks |> List.map (fun b -> b.Id)
            let slots = ids |> List.mapi (fun i id -> (id, 0.0, 900.0 + float i * 50.0, 200.0, 40.0, false))
            // 54 px into the margin: 15.2 px per frame of 60 Hz
            let scrolling, _ = send [ Studio.DragPress(Studio.DragBlock ids.[0], 20.0, 920.0); Studio.DragMove(20.0, 990.0, slots, tall) ] two
            let at hz = { scrolling with Following = Some 1000.0 }, 1000.0 + 1000.0 / hz
            let scrolledAt hz = let m, now = at hz in snd (Studio.update (Studio.Frame(tall, slots, now)) m)
            Expect.equal (scrolledAt 60.0) [ Studio.ScrollBy 16.0; Studio.NextFrame ] "60 Hz: 15.2 px, rounded up"
            Expect.equal (scrolledAt 120.0) [ Studio.ScrollBy 8.0; Studio.NextFrame ] "120 Hz: half of it in a frame of half the length (7.6 px)"
            Expect.equal (scrolledAt 30.0) [ Studio.ScrollBy 31.0; Studio.NextFrame ] "30 Hz: twice (30.4 px)"
            Expect.equal (scrolledAt 2.0) [ Studio.ScrollBy 46.0; Studio.NextFrame ] "a frame after half a second scrolls the three frames' worth that is the most (45.6 px)"
        testCase "the frames stop when the pointer leaves the margin, or lets go, or the drag is cancelled, and a later drag asks again" <| fun () ->
            let two, _ = send [ Studio.Add Heading; Studio.Add Paragraph; Studio.Select None ] start
            let ids = two.Doc.Blocks |> List.map (fun b -> b.Id)
            let slots = ids |> List.mapi (fun i id -> (id, 0.0, 900.0 + float i * 50.0, 200.0, 40.0, false))
            let scrolling, _ = send [ Studio.DragPress(Studio.DragBlock ids.[0], 20.0, 920.0); Studio.DragMove(20.0, 985.0, slots, tall) ] two
            Expect.isTrue scrolling.Following.IsSome "a frame is pending"
            let left, _ = send [ Studio.DragMove(20.0, 500.0, slots, tall) ] scrolling
            let ended, fx = send [ Studio.Frame(tall, slots, 1000.0) ] left
            Expect.equal fx [] "the pointer is in the middle again: no scroll, no next frame"
            Expect.isNone ended.Following "the loop is over"
            let released, _ = send [ Studio.DragRelease(20.0, 985.0, slots) ] scrolling
            let afterRelease, fx = send [ Studio.Frame(tall, slots, 1000.0) ] released
            Expect.equal fx [] "nothing is dragged any more"
            Expect.isNone afterRelease.Following "the pending frame ends it"
            let cancelled, _ = send [ Studio.DragCancel ] scrolling
            Expect.isNone (fst (send [ Studio.Frame(tall, slots, 1000.0) ] cancelled)).Following "the same after escape"
            let again, fx = send [ Studio.DragPress(Studio.DragBlock ids.[0], 20.0, 920.0); Studio.DragMove(20.0, 985.0, slots, tall) ] ended
            Expect.equal fx [ Studio.NextFrame ] "a drag after that asks again"
            Expect.isSome again.Following "pending"
        testCase "at the top edge the page scrolls up, and the drop is read again with the places the page measures after each scroll" <| fun () ->
            let two, _ = send [ Studio.Add Heading; Studio.Add Paragraph; Studio.Select None ] start
            let ids = two.Doc.Blocks |> List.map (fun b -> b.Id)
            let slots = ids |> List.mapi (fun i id -> (id, 0.0, float i * 50.0, 200.0, 40.0, false))
            let top, _ = send [ Studio.DragPress(Studio.DragBlock ids.[0], 20.0, 20.0); Studio.DragMove(20.0, 10.0, slots, tall) ] two
            let frame, fx = send [ Studio.Frame(tall, slots, 1000.0) ] { top with Following = Some 0.0 }
            Expect.equal fx [ Studio.ScrollBy -16.0; Studio.NextFrame ] "54 px into the margin: 15.2 up, rounded away from zero to whole pixels"
            let pointerOnSecond, _ = send [ Studio.DragMove(20.0, 85.0, slots, tall) ] { frame with Following = Some 0.0 }
            Expect.isSome (Studio.dropMark pointerOnSecond) "over the lower half of the second block: a mark behind it"
            let scrolledAway, _ = send [ Studio.Frame(tall, [], 1000.0) ] pointerOnSecond
            Expect.isNone (Studio.dropMark scrolledAway) "the places moved from under the pointer: no mark any more (and the pointer is in the middle: the loop ends)"
        testCase "while a block is dragged the view has the mark and the ghost; neither stays after the drop" <| fun () ->
            let two, _ = send [ Studio.Add Heading; Studio.Add Paragraph ] start
            let ids = two.Doc.Blocks |> List.map (fun b -> b.Id)
            let slots = ids |> List.mapi (fun i id -> (id, 0.0, float i * 50.0, 200.0, 40.0, false))
            let dragging, _ = send [ Studio.DragPress(Studio.DragBlock ids.[0], 20.0, 20.0); Studio.DragMove(20.0, 90.0, slots, tall) ] two
            Expect.isTrue (Studio.dragActive dragging) "active"
            Expect.isTrue (Studio.beingDragged dragging ids.[0]) "the first is the one dragged"
            Expect.isSome (Studio.dropMark dragging) "a mark where it would drop (behind the second)"
            Expect.isSome (Studio.dragGhost dragging) "a ghost"
            Expect.isFalse (Studio.newBeingDragged dragging Heading) "a block of the list is not a new block of the palette"
            let fromPalette, _ = send [ Studio.DragPress(Studio.DragNew Quote, 20.0, 20.0); Studio.DragMove(20.0, 90.0, slots, tall) ] two
            Expect.isTrue (Studio.newBeingDragged fromPalette Quote) "the palette's quote is the one dragged"
            Expect.isFalse (Studio.newBeingDragged fromPalette Heading) "and no other kind"
            Expect.isFalse (Studio.newBeingDragged two Quote) "nothing is dragged before the press"
            let over, _ = send [ Studio.DragMove(20.0, 10.0, slots, tall) ] dragging
            Expect.isNone (Studio.dropMark over) "over itself the drop would change nothing: no mark"
            let dropped, _ = send [ Studio.DragRelease(20.0, 90.0, slots) ] dragging
            Expect.isFalse (Studio.dragActive dropped) "idle again"
            Expect.isNone (Studio.dropMark dropped) "no mark"
        testCase "a press that does not move is a click: nothing changes" <| fun () ->
            let two, _ = send [ Studio.Add Heading; Studio.Add Paragraph ] start
            let ids = two.Doc.Blocks |> List.map (fun b -> b.Id)
            let slots = ids |> List.mapi (fun i id -> (id, 0.0, float i * 50.0, 200.0, 40.0, false))
            let clicked, fx = send [ Studio.DragPress(Studio.DragBlock ids.[0], 20.0, 20.0); Studio.DragMove(22.0, 21.0, slots, tall); Studio.DragRelease(22.0, 21.0, slots) ] two
            Expect.equal clicked.Doc two.Doc "the document is as it was"
            Expect.equal clicked.History.Past.Length two.History.Past.Length "no step"
            Expect.equal fx [] "nothing saved"
        testCase "escape cancels a drag, and dropping a block where it is changes nothing" <| fun () ->
            let two, _ = send [ Studio.Add Heading; Studio.Add Paragraph ] start
            let ids = two.Doc.Blocks |> List.map (fun b -> b.Id)
            let slots = ids |> List.mapi (fun i id -> (id, 0.0, float i * 50.0, 200.0, 40.0, false))
            let cancelled, _ = send [ Studio.DragPress(Studio.DragBlock ids.[0], 20.0, 20.0); Studio.DragMove(20.0, 90.0, slots, tall); Studio.DragCancel ] two
            Expect.isFalse (Studio.dragActive cancelled) "idle"
            Expect.equal cancelled.Doc two.Doc "unchanged"
            has cancelled.Notice "cancelled"
            // behind the first block, where it already is
            let same, fx = send [ Studio.DragPress(Studio.DragBlock ids.[0], 20.0, 20.0); Studio.DragMove(20.0, 30.0, slots, tall); Studio.DragRelease(20.0, 30.0, slots) ] two
            Expect.equal same.Doc two.Doc "unchanged"
            has same.Notice "where it was"
            // 30 px under the top of the window the page would scroll up: a frame is asked for, and that is all
            Expect.isFalse (fx |> List.exists (fun e -> match e with Studio.Save _ | Studio.SaveIndex _ -> true | _ -> false)) "nothing saved"
        // ── dragging a new block from the palette ──
        testCase "dragging a block kind from the palette puts a new block where it is dropped, selected, in one step of the log" <| fun () ->
            let three, _ = send [ Studio.Add Heading; Studio.Add Paragraph; Studio.Add Divider; Studio.Select None ] start
            let ids = three.Doc.Blocks |> List.map (fun b -> b.Id)
            let slots = ids |> List.mapi (fun i id -> (id, 0.0, float i * 50.0, 200.0, 40.0, false))
            // the palette is elsewhere on the page (x = 500): the press is not over a block; the drop is on the upper half of the second
            let added, fx =
                send [ Studio.DragPress(Studio.DragNew Quote, 500.0, 20.0)
                       Studio.DragMove(300.0, 60.0, slots, tall)
                       Studio.DragMove(20.0, 55.0, slots, tall)
                       Studio.DragRelease(20.0, 55.0, slots) ] three
            let kinds = added.Doc.Blocks |> List.map (fun b -> b.Kind)
            Expect.equal kinds [ Heading; Quote; Paragraph; Divider ] "the quote is between the first and the second"
            Expect.equal added.Selected (Some (added.Doc.Blocks.[1].Id)) "and selected"
            Expect.equal added.Doc.Blocks.[1].Id three.Doc.NextId "it has the number that was next"
            Expect.equal added.Doc.NextId (three.Doc.NextId + 1) "the next number moved on"
            has added.Notice "added as"
            has added.Notice "by dragging"
            Expect.isTrue (fx |> List.exists (fun e -> match e with Studio.Save _ -> true | _ -> false)) "saved"
            Expect.equal (added.History.Past.Length - three.History.Past.Length) 1 "one step"
            let back, _ = send [ Studio.Undo ] added
            Expect.equal back.Doc three.Doc "undo takes it out again, and the numbering is as it was"
        testCase "while a new block is dragged the view has the mark and a ghost with the kind's name" <| fun () ->
            let two, _ = send [ Studio.Add Heading; Studio.Add Paragraph ] start
            let ids = two.Doc.Blocks |> List.map (fun b -> b.Id)
            let slots = ids |> List.mapi (fun i id -> (id, 0.0, float i * 50.0, 200.0, 40.0, false))
            let over, _ = send [ Studio.DragPress(Studio.DragNew Divider, 500.0, 20.0); Studio.DragMove(20.0, 10.0, slots, tall) ] two
            Expect.isSome (Studio.dropMark over) "a mark, even over the first block: a new block always changes the document"
            Expect.equal (Studio.dragGhost over |> Option.map (fun (label, _, _) -> label)) (Some "New divider") "the ghost says what is coming"
            Expect.isFalse (Studio.beingDragged over ids.[0]) "no block of the list is dimmed"
            Expect.isTrue (Studio.dragging over) "a drag, not a press"
            let pressed, _ = send [ Studio.DragPress(Studio.DragNew Divider, 500.0, 20.0) ] two
            Expect.isFalse (Studio.dragging pressed) "a press that has not moved is not a drag yet"
        testCase "a new block released outside the blocks, or cancelled with Escape, adds nothing; a press that does not move is left to the click" <| fun () ->
            let two, _ = send [ Studio.Add Heading; Studio.Add Paragraph ] start
            let ids = two.Doc.Blocks |> List.map (fun b -> b.Id)
            let slots = ids |> List.mapi (fun i id -> (id, 0.0, float i * 50.0, 200.0, 40.0, false))
            let outside, _ = send [ Studio.DragPress(Studio.DragNew Spacer, 500.0, 20.0); Studio.DragMove(520.0, 400.0, [], tall); Studio.DragRelease(520.0, 400.0, []) ] two
            Expect.equal outside.Doc two.Doc "nothing added"
            has outside.Notice "nothing added"
            let cancelled, _ = send [ Studio.DragPress(Studio.DragNew Spacer, 500.0, 20.0); Studio.DragMove(20.0, 90.0, slots, tall); Studio.DragCancel ] two
            Expect.equal cancelled.Doc two.Doc "unchanged"
            Expect.isFalse (Studio.dragActive cancelled) "idle"
            has cancelled.Notice "Adding cancelled"
            let clicked, fx = send [ Studio.DragPress(Studio.DragNew Spacer, 500.0, 20.0); Studio.DragRelease(501.0, 21.0, slots) ] two
            Expect.equal clicked.Doc two.Doc "a click adds through the button's own click, not through the drop"
            Expect.equal fx [] "nothing saved"
        testCase "a new block dropped on the empty list is the first block" <| fun () ->
            let slots = [ (Dnd.endOfList, 0.0, 0.0, 200.0, 60.0, false) ]
            let first, _ = send [ Studio.DragPress(Studio.DragNew Footer, 500.0, 20.0); Studio.DragMove(100.0, 30.0, slots, tall); Studio.DragRelease(100.0, 30.0, slots) ] start
            Expect.equal (first.Doc.Blocks |> List.map (fun b -> b.Kind)) [ Footer ] "one block"
            Expect.equal first.Selected (Some first.Doc.Blocks.[0].Id) "selected"
        // ── what the preview frame says ──
        testCase "a click in the preview selects the block, and what it says about itself is kept" <| fun () ->
            let m, _ = send [ Studio.Add Heading; Studio.Add Paragraph; Studio.Select None ] start
            let id = m.Doc.Blocks.[1].Id
            let sel, _ = send [ Studio.FromPreview (sprintf "{\"block\":\"%d\"}" id) ] m
            Expect.equal sel.Selected (Some id) "the block that was clicked"
            Expect.equal sel.Panel Studio.PBlock "its settings are shown"
            let none, fx = send [ Studio.FromPreview "{\"block\":\"99\"}"; Studio.FromPreview "not json"; Studio.FromPreview "[1,2]"; Studio.FromPreview "{}" ] m
            Expect.equal none.Doc m.Doc "nothing else changes the document"
            Expect.equal fx [] "and nothing is said back"
            let scrolled, _ = send [ Studio.FromPreview "{\"scroll\":120}" ] m
            let _, back = send [ Studio.FromPreview "{\"ready\":true}" ] scrolled
            Expect.equal back [ Studio.ToPreview "{\"scroll\":120}" ] "a document that has loaded is put back where it was"
        testCase "a text edited in place: the preview is kept as it was, the typing is one step of the log, and the preview follows again afterwards" <| fun () ->
            let m, _ = send [ Studio.Add Heading; Studio.Select None ] start
            let b = m.Doc.Blocks.[0]
            let before = Studio.previewDocument m
            let id = string b.Id
            let editing, _ = send [ Studio.FromPreview (sprintf "{\"editing\":true,\"block\":\"%s\"}" id) ] m
            Expect.isSome editing.Frozen "the editor is open"
            let typed, fx =
                send [ Studio.FromPreview (sprintf "{\"edit\":{\"block\":\"%s\",\"value\":\"N\"}}" id)
                       Studio.FromPreview (sprintf "{\"edit\":{\"block\":\"%s\",\"value\":\"New\"}}" id)
                       Studio.FromPreview (sprintf "{\"edit\":{\"block\":\"%s\",\"value\":\"New title\"}}" id) ] editing
            Expect.equal typed.Doc.Blocks.[0].Text "New title" "the text of the block is what was typed"
            Expect.equal (Studio.previewDocument typed) before "the preview is not rendered again while the editor is open"
            Expect.isTrue (fx |> List.exists (fun e -> match e with Studio.Save _ -> true | _ -> false)) "and the project is kept"
            Expect.equal (typed.History.Past.Length - m.History.Past.Length) 1 "one step of the log for the whole typing"
            let closed, _ = send [ Studio.FromPreview "{\"editing\":false}" ] typed
            has (Studio.previewDocument closed) "New title"
            Expect.isNone closed.Frozen "the preview follows the document again"
            let back, _ = send [ Studio.Undo ] closed
            Expect.equal back.Doc.Blocks.[0].Text b.Text "undo takes the typing back at once"
        testCase "anything else ends the editing, a block without a text and an unknown block are not edited, a number is a block as well" <| fun () ->
            let m, _ = send [ Studio.Add Heading; Studio.Add Divider; Studio.Select None ] start
            let h, d = m.Doc.Blocks.[0], m.Doc.Blocks.[1]
            let editing, _ = send [ Studio.FromPreview (sprintf "{\"editing\":true,\"block\":%d}" h.Id) ] m
            let moved, _ = send [ Studio.SetPanel Studio.PTheme ] editing
            Expect.isNone moved.Frozen "a message that is not the frame's ends it: the preview is rendered again"
            let kept, _ = send [ Studio.FromPreview "{\"scroll\":5}" ] editing
            Expect.isSome kept.Frozen "what the frame says while it holds the editor does not"
            let ignored, fx = send [ Studio.FromPreview (sprintf "{\"edit\":{\"block\":\"%d\",\"value\":\"x\"}}" d.Id); Studio.FromPreview "{\"edit\":{\"block\":\"77\",\"value\":\"x\"}}" ] m
            Expect.equal ignored.Doc m.Doc "a divider has no text, and block 77 is none"
            Expect.equal fx [] "nothing saved"
        testCase "the preview marks the text that is edited in place, an export does not" <| fun () ->
            let d = { emptyDocument with Blocks = [ { blank Heading 1 with Text = "Hi \"you\" & co" }; blank Divider 2; { blank Paragraph 3 with Text = "One.\n\nTwo." } ]; NextId = 4 }
            let o : Render.Options = { Render.exportOptions with Preview = true; Selected = 1 }
            for html in [ Render.email o defaultProfile d; Render.web o defaultProfile d ] do
                has html "data-edit-text=\"Hi &quot;you&quot; &amp; co\""
                has html "data-edit-text=\"One.\n\nTwo.\""
                Expect.equal (occurrences "data-edit-text=\"" html) 2 "the heading and the paragraph, not the divider"
                has html "'__edit'"
            hasNot (Render.email Render.exportOptions defaultProfile d) "data-edit-text"
            hasNot (Render.web Render.exportOptions defaultProfile d) "data-edit-text"
            hasNot (Render.web Render.exportOptions defaultProfile d) "data-block"
            has (Render.web o defaultProfile d) "<div data-block=\"3\""
        // ── the library of projects ──
        testCase "a first visit makes a library of one, from the sample, and keeps it" <| fun () ->
            let m, fx = send [ Studio.Boot("", "", "", 1000.0) ] Studio.init
            Expect.equal (m.Library.Items |> List.map (fun p -> p.Id)) [ "p1" ] "one project"
            Expect.equal m.Library.Current "p1" "and it is open"
            Expect.equal m.Doc.Name Sample.issue.Name "the sample"
            Expect.isTrue (fx |> List.exists (fun e -> match e with Studio.Save("p1", _) -> true | _ -> false)) "its project is kept"
            Expect.isTrue (fx |> List.exists (fun e -> match e with Studio.SaveIndex _ -> true | _ -> false)) "and the index"
        testCase "the single project of the versions before is migrated, and its old key forgotten" <| fun () ->
            let legacy = Codec.projectToJson { emptyDocument with Name = "Old work"; Subject = "kept" } bootstrap
            let m, fx = send [ Studio.Boot("", "", legacy, 5.0) ] Studio.init
            Expect.equal m.Doc.Name "Old work" "the document came with it"
            Expect.equal (m.Library.Items |> List.map (fun p -> p.Name)) [ "Old work" ] "it is the library's first project"
            Expect.isTrue (fx |> List.contains (Studio.Erase Projects.legacyKey)) "the old key is erased"
        testCase "a library that was kept opens the project it was left on, without saving it again" <| fun () ->
            let lib : Projects.Library = { Items = [ { Id = "p1"; Name = "A"; Subject = ""; Blocks = 0; Created = 1.0; Modified = 1.0 }; { Id = "p2"; Name = "B"; Subject = "s"; Blocks = 0; Created = 2.0; Modified = 2.0 } ]; Current = "p1" }
            let b = Codec.projectToJson { emptyDocument with Name = "A" } bootstrap
            let m, fx = send [ Studio.Boot(Projects.libraryToJson lib, b, "", 9.0) ] Studio.init
            Expect.equal m.Library.Current "p1" "the one it was left on"
            Expect.equal m.Doc.Name "A" "its document"
            Expect.equal fx [] "nothing is written"
            let gone, fx2 = send [ Studio.Boot(Projects.libraryToJson { lib with Current = "p9" }, "", "", 9.0) ] Studio.init
            Expect.equal gone.Library.Current "" "nothing is open until the newest has been read"
            Expect.equal fx2 [ Studio.Load("p2", Studio.OpenIt) ] "the project it was left on is not in the library any more: the newest is opened"
        testCase "a change is kept under the id of the open project, and `Saved` stamps it" <| fun () ->
            let m0, _ = send [ Studio.Boot("", "", "", 100.0) ] { Studio.init with Doc = emptyDocument }
            let m1, fx = send [ Studio.EditDoc(DName, "Renamed"); Studio.EditDoc(DSubject, "Hello") ] m0
            Expect.isTrue (fx |> List.exists (fun e -> match e with Studio.Save(id, _) -> id = m0.Library.Current | _ -> false)) "kept under its id"
            Expect.equal (m1.Library.Items |> List.map (fun p -> p.Name, p.Subject)) [ "Renamed", "Hello" ] "the list shows the new name and subject at once"
            let m2, fx2 = send [ Studio.Saved(m0.Library.Current, 777.0) ] m1
            Expect.equal (Projects.find m0.Library.Current m2.Library).Value.Modified 777.0 "stamped"
            Expect.isTrue (fx2 |> List.exists (fun e -> match e with Studio.SaveIndex _ -> true | _ -> false)) "and the index is kept"
        testCase "the time of a save does not take the editor out of the preview that holds it" <| fun () ->
            let m0, _ = send [ Studio.Boot("", "", "", 100.0); Studio.Add Paragraph ] { Studio.init with Doc = emptyDocument }
            let id = m0.Library.Current
            let editing, _ = send [ Studio.FromPreview "{\"editing\":true}" ] m0
            Expect.isSome editing.Frozen "the editor is open"
            let after, _ = send [ Studio.Saved(id, 5.0) ] editing
            Expect.isSome after.Frozen "the answer of the host that the project was kept leaves it open"
        testCase "a new project is opened, kept, and takes the theme along; the one before stays as it was" <| fun () ->
            let m0, _ = send [ Studio.Boot("", "", "", 100.0); Studio.PickProfile "Tailwind CSS 3" ] { Studio.init with Doc = emptyDocument }
            let m1, fx = send [ Studio.NewProject ] m0
            Expect.equal m1.Library.Items.Length 2 "two projects"
            Expect.equal m1.Library.Current "p2" "the new one is open"
            Expect.equal m1.Doc.Blocks [] "empty"
            Expect.equal m1.Profile.Name "Tailwind CSS 3" "with the theme of the one before"
            Expect.equal m1.Doc.Name (Projects.numbered "Untitled issue" m0.Library.Items) "named so that no two are alike"
            Expect.isTrue (fx |> List.exists (fun e -> match e with Studio.Save("p2", _) -> true | _ -> false)) "kept at once"
            Expect.isFalse (Studio.canUndo m1) "a fresh log of changes"
        testCase "opening another project asks the host to read it, and the answer replaces the document" <| fun () ->
            let m0, _ = send [ Studio.Boot("", "", "", 100.0); Studio.EditDoc(DName, "First"); Studio.NewProject; Studio.EditDoc(DName, "Second") ] { Studio.init with Doc = emptyDocument }
            let m1, fx = send [ Studio.OpenProject "p1" ] m0
            Expect.equal fx [ Studio.Load("p1", Studio.OpenIt) ] "read it"
            Expect.equal m1.Library.Current "p2" "the open one stays open until the answer is there (a change in between is not kept under another id)"
            let json = Codec.projectToJson { emptyDocument with Name = "First"; Subject = "back" } bootstrap
            let m2, fx2 = send [ Studio.Loaded("p1", Studio.OpenIt, Some json) ] m1
            Expect.equal (m2.Library.Current, m2.Doc.Subject) ("p1", "back") "opened"
            Expect.isFalse m2.ProjectsOpen "the list closes"
            Expect.isFalse (fx2 |> List.exists (fun e -> match e with Studio.Save _ -> true | _ -> false)) "what was read is not kept again"
            let m3, _ = send [ Studio.Loaded("p9", Studio.OpenIt, None) ] m2
            Expect.equal m3.Library.Current "p1" "a project that cannot be read changes nothing"
            has m3.Notice "could not be read"
        testCase "duplicating: the open one at once, another through the host; the copy is its own project" <| fun () ->
            let m0, _ = send [ Studio.Boot("", "", "", 100.0); Studio.EditDoc(DName, "Issue") ] { Studio.init with Doc = emptyDocument }
            let m1, fx = send [ Studio.DuplicateProject "p1" ] m0
            Expect.equal (m1.Library.Items |> List.map (fun p -> p.Name) |> List.sort) [ "Issue"; "Issue (copy)" ] "a copy beside it"
            Expect.equal m1.Library.Current "p1" "the one that was open stays open"
            Expect.isTrue (fx |> List.exists (fun e -> match e with Studio.Save("p2", _) -> true | _ -> false)) "the copy is kept"
            let _, fx2 = send [ Studio.DuplicateProject "p2" ] m1
            Expect.equal fx2 [ Studio.Load("p2", Studio.CopyIt) ] "another project is read first"
            let json = Codec.projectToJson { emptyDocument with Name = "Issue (copy)" } bootstrap
            let m3, _ = send [ Studio.Loaded("p2", Studio.CopyIt, Some json) ] m1
            Expect.equal (m3.Library.Items |> List.map (fun p -> p.Name) |> List.sort) [ "Issue"; "Issue (copy)"; "Issue (copy) (copy)" ] "a copy of a copy"
        testCase "deleting another project erases it; deleting the open one opens the newest of the rest; the last one gives a new empty project" <| fun () ->
            let m0, _ = send [ Studio.Boot("", "", "", 100.0); Studio.EditDoc(DName, "One"); Studio.NewProject; Studio.EditDoc(DName, "Two"); Studio.Saved("p2", 500.0) ] { Studio.init with Doc = emptyDocument }
            let m1, fx = send [ Studio.AskDelete (Some "p1"); Studio.DeleteProject "p1" ] m0
            Expect.equal (m1.Library.Items |> List.map (fun p -> p.Id)) [ "p2" ] "gone from the list"
            Expect.isTrue (fx |> List.contains (Studio.Erase(Projects.projectKey "p1"))) "and from the storage"
            Expect.equal m1.Library.Current "p2" "the open one stays open"
            Expect.equal m1.Asking None "the question is closed"
            let m2, fx2 = send [ Studio.DeleteProject "p2" ] m1
            Expect.equal m2.Library.Items.Length 1 "the last one is replaced by a new empty project"
            Expect.equal m2.Doc.Blocks [] "empty"
            Expect.isTrue (fx2 |> List.contains (Studio.Erase(Projects.projectKey "p2"))) "the deleted one is erased"
            let three, _ = send [ Studio.Boot("", "", "", 100.0); Studio.EditDoc(DName, "A"); Studio.Saved("p1", 100.0); Studio.NewProject; Studio.EditDoc(DName, "B"); Studio.Saved("p2", 200.0); Studio.NewProject ] { Studio.init with Doc = emptyDocument }
            let after, fx3 = send [ Studio.DeleteProject "p3" ] three
            Expect.equal after.Library.Current "" "nothing is open until the newest of the rest has been read"
            Expect.isTrue (fx3 |> List.contains (Studio.Load("p2", Studio.OpenIt))) "the newest by when it was saved"
            let edited, fx4 = send [ Studio.EditDoc(DName, "typed in between") ] after
            Expect.isTrue (fx4 |> List.exists (fun e -> match e with Studio.Save("", _) -> true | _ -> false)) "a change before the answer is kept under no id (the host drops it), not under another project's"
            Expect.equal edited.Library.Items.Length 2 "and the library is as it was"
        testCase "import as a project adds and opens a project; a profile alone is refused" <| fun () ->
            let project = Codec.projectToJson { Sample.issue with Name = "Imported" } tailwind
            let m0, _ = send [ Studio.Boot("", "", "", 100.0) ] Studio.init
            let m1, fx = send [ Studio.ImportChanged project; Studio.ImportAsProject ] m0
            Expect.equal m1.Library.Items.Length 2 "two projects"
            Expect.equal (m1.Doc.Name, m1.Profile.Name) ("Imported", "Tailwind CSS 3") "opened, with its theme"
            Expect.equal m1.ImportText "" "the field is emptied"
            Expect.isTrue (fx |> List.exists (fun e -> match e with Studio.Save(id, _) -> id = m1.Library.Current | _ -> false)) "kept"
            let bad, _ = send [ Studio.ImportChanged "{\"x\":1}"; Studio.ImportAsProject ] m0
            Expect.equal bad.Library.Items.Length 1 "nothing added"
            Expect.isTrue (bad.ImportError <> "") "and it says why"
        testCase "the search filters what the list shows" <| fun () ->
            let m0, _ = send [ Studio.Boot("", "", "", 100.0); Studio.EditDoc(DName, "Weekly"); Studio.NewProject; Studio.EditDoc(DName, "Launch") ] { Studio.init with Doc = emptyDocument }
            Expect.equal (Studio.visibleProjects m0).Length 2 "both"
            let m1, _ = send [ Studio.FilterProjects "weak" ] m0
            Expect.equal (Studio.visibleProjects m1).Length 0 "none"
            let m2, _ = send [ Studio.FilterProjects "LAUN" ] m0
            Expect.equal (Studio.visibleProjects m2 |> List.map (fun p -> p.Name)) [ "Launch" ] "one"
    ]

// ───────────────────────────── projects ─────────────────�
let projects =
    let meta (id: string) (name: string) (modified: float) : Projects.Meta = { Id = id; Name = name; Subject = ""; Blocks = 0; Created = modified; Modified = modified }
    testList "Projects" [
        testCase "ids: one above the largest number in use, never twice" <| fun () ->
            Expect.equal (Projects.nextId []) "p1" "the first"
            Expect.equal (Projects.nextId [ meta "p1" "a" 0.0; meta "p7" "b" 0.0; meta "x" "c" 0.0 ]) "p8" "above the largest; one that is not p<n> counts as 0"
        testCase "names: numbered for a new project, (copy) for a copy" <| fun () ->
            let items = [ meta "p1" "Untitled issue" 0.0; meta "p2" "Untitled issue 2" 0.0 ]
            Expect.equal (Projects.numbered "Untitled issue" items) "Untitled issue 3" "the first free number"
            Expect.equal (Projects.numbered "Other" items) "Other" "free as it is"
            Expect.equal (Projects.copyName "Issue" [ meta "p1" "Issue" 0.0 ]) "Issue (copy)" "a copy"
            Expect.equal (Projects.copyName "Issue" [ meta "p1" "Issue" 0.0; meta "p2" "Issue (copy)" 0.0 ]) "Issue (copy 2)" "a copy of a copy's name"
        testCase "the list is newest first, and the search matches name or subject in any case" <| fun () ->
            let a = { meta "p1" "Weekly #1" 100.0 with Subject = "Small software" }
            let b = meta "p2" "Launch" 300.0
            let c = meta "p3" "Weekly #2" 200.0
            let lib : Projects.Library = { Items = [ a; b; c ]; Current = "p1" }
            Expect.equal (Projects.visible "" lib |> List.map (fun m -> m.Id)) [ "p2"; "p3"; "p1" ] "by when they were saved"
            Expect.equal (Projects.visible "weekly" lib |> List.map (fun m -> m.Id)) [ "p3"; "p1" ] "a part of the name, whatever the case"
            Expect.equal (Projects.visible "SOFTWARE" lib |> List.map (fun m -> m.Id)) [ "p1" ] "or of the subject"
            Expect.equal (Projects.visible "  " lib).Length 3 "nothing typed matches all"
        testCase "upsert replaces by id or adds first; remove drops" <| fun () ->
            let lib : Projects.Library = { Items = [ meta "p1" "a" 1.0 ]; Current = "p1" }
            let lib2 = Projects.upsert (meta "p1" "renamed" 5.0) lib
            Expect.equal lib2.Items [ meta "p1" "renamed" 5.0 ] "replaced"
            let lib3 = Projects.upsert (meta "p2" "b" 6.0) lib2
            Expect.equal (lib3.Items |> List.map (fun m -> m.Id)) [ "p2"; "p1" ] "added in front"
            Expect.equal (Projects.remove "p1" lib3).Items [ meta "p2" "b" 6.0 ] "dropped"
        testCase "the index round-trips, and a damaged one is read as far as it goes" <| fun () ->
            let lib : Projects.Library = { Items = [ { meta "p1" "Ünïcode \"quoted\"" 1.7e12 with Subject = "S"; Blocks = 4 }; meta "p2" "b" 2.0 ]; Current = "p2" }
            Expect.equal (Projects.libraryOfJson (Projects.libraryToJson lib)) (Some lib) "round trip"
            Expect.equal (Projects.libraryOfJson "{\"projects\":[{\"id\":\"p1\",\"name\":\"ok\"},{\"nope\":1},5]}") (Some ({ Items = [ { Id = "p1"; Name = "ok"; Subject = ""; Blocks = 0; Created = 0.0; Modified = 0.0 } ]; Current = "" } : Projects.Library)) "entries that are no project are skipped"
            Expect.equal (Projects.libraryOfJson "{{{") None "not JSON"
            Expect.equal (Projects.libraryOfJson "{\"x\":1}") None "not a library"
        testCase "stamp: the date and time in UTC from milliseconds, around the leap day and the year's end" <| fun () ->
            Expect.equal (Projects.stamp 0.0) "" "unknown"
            Expect.equal (Projects.stamp 1.0) "1970-01-01 00:00" "the epoch"
            Expect.equal (Projects.stamp 951782400000.0) "2000-02-29 00:00" "a leap day"
            Expect.equal (Projects.stamp 1798742100000.0) "2026-12-31 18:35" "the end of a year"
            Expect.equal (Projects.stamp 1785594000000.0) "2026-08-01 14:20" "a date in the middle"
            Expect.equal (Projects.stamp 4102444799000.0) "2099-12-31 23:59" "far ahead"
    ]

let all : Test list = [ model; color; markup; render; codec; lint; studio; projects ]
