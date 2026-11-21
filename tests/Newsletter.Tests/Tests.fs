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
                       Studio.DragMove(20.0, 60.0, slots)                            // beyond the threshold, over the second
                       Studio.DragMove(20.0, 128.0, slots)                           // over the lower half of the third
                       Studio.DragRelease(20.0, 128.0, slots) ] three
            Expect.equal (moved.Doc.Blocks |> List.map (fun b -> b.Id)) [ ids.[1]; ids.[2]; ids.[0] ] "the first block is last now"
            has moved.Notice "moved by dragging"
            Expect.isTrue (fx |> List.exists (fun e -> match e with Studio.Save _ -> true | _ -> false)) "saved"
            Expect.equal (moved.History.Past.Length - three.History.Past.Length) 1 "one step"
            let back, _ = send [ Studio.Undo ] moved
            Expect.equal (back.Doc.Blocks |> List.map (fun b -> b.Id)) ids "undo puts it back"
        testCase "while a block is dragged the view has the mark and the ghost; neither stays after the drop" <| fun () ->
            let two, _ = send [ Studio.Add Heading; Studio.Add Paragraph ] start
            let ids = two.Doc.Blocks |> List.map (fun b -> b.Id)
            let slots = ids |> List.mapi (fun i id -> (id, 0.0, float i * 50.0, 200.0, 40.0, false))
            let dragging, _ = send [ Studio.DragPress(Studio.DragBlock ids.[0], 20.0, 20.0); Studio.DragMove(20.0, 90.0, slots) ] two
            Expect.isTrue (Studio.dragActive dragging) "active"
            Expect.isTrue (Studio.beingDragged dragging ids.[0]) "the first is the one dragged"
            Expect.isSome (Studio.dropMark dragging) "a mark where it would drop (behind the second)"
            Expect.isSome (Studio.dragGhost dragging) "a ghost"
            let over, _ = send [ Studio.DragMove(20.0, 10.0, slots) ] dragging
            Expect.isNone (Studio.dropMark over) "over itself the drop would change nothing: no mark"
            let dropped, _ = send [ Studio.DragRelease(20.0, 90.0, slots) ] dragging
            Expect.isFalse (Studio.dragActive dropped) "idle again"
            Expect.isNone (Studio.dropMark dropped) "no mark"
        testCase "a press that does not move is a click: nothing changes" <| fun () ->
            let two, _ = send [ Studio.Add Heading; Studio.Add Paragraph ] start
            let ids = two.Doc.Blocks |> List.map (fun b -> b.Id)
            let slots = ids |> List.mapi (fun i id -> (id, 0.0, float i * 50.0, 200.0, 40.0, false))
            let clicked, fx = send [ Studio.DragPress(Studio.DragBlock ids.[0], 20.0, 20.0); Studio.DragMove(22.0, 21.0, slots); Studio.DragRelease(22.0, 21.0, slots) ] two
            Expect.equal clicked.Doc two.Doc "the document is as it was"
            Expect.equal clicked.History.Past.Length two.History.Past.Length "no step"
            Expect.equal fx [] "nothing saved"
        testCase "escape cancels a drag, and dropping a block where it is changes nothing" <| fun () ->
            let two, _ = send [ Studio.Add Heading; Studio.Add Paragraph ] start
            let ids = two.Doc.Blocks |> List.map (fun b -> b.Id)
            let slots = ids |> List.mapi (fun i id -> (id, 0.0, float i * 50.0, 200.0, 40.0, false))
            let cancelled, _ = send [ Studio.DragPress(Studio.DragBlock ids.[0], 20.0, 20.0); Studio.DragMove(20.0, 90.0, slots); Studio.DragCancel ] two
            Expect.isFalse (Studio.dragActive cancelled) "idle"
            Expect.equal cancelled.Doc two.Doc "unchanged"
            has cancelled.Notice "cancelled"
            // behind the first block, where it already is
            let same, fx = send [ Studio.DragPress(Studio.DragBlock ids.[0], 20.0, 20.0); Studio.DragMove(20.0, 30.0, slots); Studio.DragRelease(20.0, 30.0, slots) ] two
            Expect.equal same.Doc two.Doc "unchanged"
            has same.Notice "where it was"
            Expect.equal fx [] "nothing saved"
    ]

let all : Test list = [ model; color; markup; render; codec; lint; studio ]
