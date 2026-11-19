/// Checks of what can go wrong for the reader: missing alternative text, low contrast, unclear links, a missing unsubscribe link,
/// a message that Gmail will clip.  Each finding names the block it is about, so the designer can jump to it.
module Newsletter.Lint

open Newsletter.Model
open Newsletter.Profile
open Newsletter.Color

type Severity = Error | Warning

type Finding = { Severity: Severity; Block: int option; Message: string }

let private err (b: Block) (m: string) = { Severity = Error; Block = Some b.Id; Message = m }
let private warn (b: Block) (m: string) = { Severity = Warning; Block = Some b.Id; Message = m }

let private vague = [ "click here"; "here"; "read more"; "more"; "link"; "this" ]

let private ratio (a: string) (b: string) : string =
    match contrast a b with Some r -> (System.Math.Round(r * 100.0) / 100.0 |> string) + " : 1" | None -> "?"

/// `Some finding` when the condition holds.  (Lists of these, then `List.choose id`: no list expressions, which the WasmGC
/// compiler does not fuse yet.)
let private when' (cond: bool) (f: Finding) : Finding option = if cond then Some f else None

let private blockFindings (p: Profile) (b: Block) : Finding list =
    let surface = token "surface" p
    match b.Kind with
    | Image ->
        [ when' (b.Alt.Trim() = "") (err b "Image without alternative text.")
          when' (b.Url.Trim() = "") (err b "Image without an address.")
          when' (b.Link.Trim() <> "" && b.Alt.Trim() = "") (err b "A linked image needs alternative text: it is the name of the link.") ]
        |> List.choose id
    | Button ->
        let bg, fg =
            match b.Variant with
            | Primary -> token "primary" p, token "onPrimary" p
            | Secondary -> token "secondary" p, token "onSecondary" p
            | Outline -> surface, token "primary" p
        [ when' (b.Url.Trim() = "") (err b "Button without a link.")
          when' (b.Text.Trim() = "") (err b "Button without a label.")
          when' (vague |> List.contains (b.Text.Trim().ToLowerInvariant())) (warn b ("The label “" + b.Text.Trim() + "” does not say where the link goes."))
          when' (not (meetsAA fg bg)) (warn b ("Label and button colour have a contrast of " + ratio fg bg + "; 4.5 : 1 is the minimum (WCAG 1.4.3).")) ]
        |> List.choose id
    | Paragraph | Columns ->
        [ when' (b.Text.Trim() = "") (warn b "Empty text.")
          when' (not (meetsAA (token "text" p) surface)) (warn b ("Text and background have a contrast of " + ratio (token "text" p) surface + ".")) ]
        |> List.choose id
    | Heading -> [ when' (b.Text.Trim() = "") (warn b "Empty heading.") ] |> List.choose id
    | Footer ->
        [ when' (b.Text2.Trim() = "") (warn b "No sender address (required by law in many countries).")
          when' (not (meetsAA (token "muted" p) surface)) (warn b ("Muted text has a contrast of " + ratio (token "muted" p) surface + " on the content background.")) ]
        |> List.choose id
    | Quote -> [ when' (b.Text.Trim() = "") (warn b "Empty quote.") ] |> List.choose id
    | Divider | Spacer -> []

/// Findings for the whole document: its own fields first, then block by block.  `htmlSize` is the size of the rendered email in bytes.
let check (p: Profile) (d: Document) (htmlSize: int) : Finding list =
    let doc (sev: Severity) (m: string) = { Severity = sev; Block = None; Message = m }
    let headings = d.Blocks |> List.filter (fun b -> b.Kind = Heading)
    let own =
        [ (if d.Subject.Trim() = "" then Some(doc Error "No subject line.")
           elif d.Subject.Length > 60 then Some(doc Warning ("The subject has " + string d.Subject.Length + " characters; mail apps cut it after about 60."))
           else None)
          (if d.Preheader.Trim() = "" then Some(doc Warning "No preheader: mail apps show the first words of the body instead.")
           elif d.Preheader.Length > 110 then Some(doc Warning "The preheader is longer than 110 characters.")
           else None)
          when' (d.Lang.Trim() = "") (doc Error "No language: screen readers need it to pick a voice.")
          when' (not (d.Blocks |> List.exists (fun b -> b.Kind = Footer))) (doc Error "No footer with an unsubscribe link.")
          (match headings with
           | first :: _ when first.Level <> 1 -> Some { Severity = Warning; Block = Some first.Id; Message = "The first heading should be level 1." }
           | _ -> None)
          when' (htmlSize > 102 * 1024) (doc Warning ("The email is " + string (htmlSize / 1024) + " KB; Gmail clips messages over 102 KB.")) ]
        |> List.choose id
    let skipped =
        List.pairwise headings
        |> List.choose (fun (a, b) ->
            when' (b.Level > a.Level + 1) { Severity = Warning; Block = Some b.Id; Message = "Heading level " + string b.Level + " follows level " + string a.Level + "; do not skip levels." })
    own @ skipped @ (d.Blocks |> List.collect (blockFindings p))

let errors (fs: Finding list) : int = fs |> List.filter (fun f -> f.Severity = Error) |> List.length
