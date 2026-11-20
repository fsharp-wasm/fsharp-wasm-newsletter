/// Documents and profiles as JSON: what is saved in the browser, exported, shared and imported.  Reading never fails on a missing
/// field (it falls back), so a document written by an older or newer version still opens.
module Newsletter.Codec

open FSharp.Wasm.Data
open Newsletter.Model
open Newsletter.Profile

let blockToJson (b: Block) : Json =
    // only the fields the kind uses, so the file reads like the document
    let used = fieldsOf b.Kind |> List.map (fun s -> s.Field)
    let has f = List.contains f used
    JObj(
        [ "type", JStr(kindKey b.Kind) ]
        @ (if has FText then [ "text", JStr b.Text ] else [])
        @ (if has FText2 then [ "text2", JStr b.Text2 ] else [])
        @ (if has FUrl then [ "url", JStr b.Url ] else [])
        @ (if has FAlt then [ "alt", JStr b.Alt ] else [])
        @ (if has FLink then [ "link", JStr b.Link ] else [])
        @ (if has FAlign then [ "align", JStr(alignKey b.Align) ] else [])
        @ (if has FLevel then [ "level", JNum(float b.Level) ] else [])
        @ (if has FSize then [ "size", JNum(float b.Size) ] else [])
        @ (if has FVariant then [ "variant", JStr(variantKey b.Variant) ] else []))

/// A block from JSON; None for an unknown type (skipped, with the rest of the document intact).
let blockOfJson (id: int) (j: Json) : Block option =
    kindOfKey (Json.stringOr "type" "" j)
    |> Option.map (fun kind ->
        let b = blank kind id
        let text = Json.stringOr "text" b.Text j
        { b with
            Text = text
            Text2 = Json.stringOr "text2" b.Text2 j
            Url = Json.stringOr "url" b.Url j
            Alt = Json.stringOr "alt" b.Alt j
            Link = Json.stringOr "link" b.Link j
            Align = alignOfKey (Json.stringOr "align" (alignKey b.Align) j)
            Level = clamp 1 3 (Json.intOr "level" b.Level j)
            Size = (if kind = Spacer then clamp 4 120 else clamp 10 100) (Json.intOr "size" b.Size j)
            Variant = variantOfKey (Json.stringOr "variant" (variantKey b.Variant) j) })

let documentToJson (d: Document) : Json =
    JObj [ "name", JStr d.Name; "subject", JStr d.Subject; "preheader", JStr d.Preheader; "lang", JStr d.Lang
           "blocks", JArr(d.Blocks |> List.map blockToJson) ]

let documentOfJson (j: Json) : Document =
    let blocks = Json.listOf "blocks" j |> List.mapi (fun i b -> blockOfJson (i + 1) b) |> List.choose id
    { Name = Json.stringOr "name" "Untitled issue" j; Subject = Json.stringOr "subject" "" j; Preheader = Json.stringOr "preheader" "" j; Lang = Json.stringOr "lang" "en" j
      Blocks = blocks; NextId = blocks.Length + 1 }

/// One file: the document and the profile it is designed with.
let projectToJson (d: Document) (p: Profile) : string =
    Json.pretty (JObj [ "format", JStr "fsharp-wasm-newsletter/1"; "document", documentToJson d; "profile", Newsletter.Profile.toJson p ])

let projectOfJson (text: string) : Result<Document * Profile, string> =
    match Json.parse text with
    | Error e -> Error("Not valid JSON: " + e)
    | Ok j ->
        match Json.field "document" j with
        | None -> Error "No \"document\" in the file."
        | Some d -> Ok(documentOfJson d, (match Json.field "profile" j with Some p -> Newsletter.Profile.ofJson p | None -> defaultProfile))

/// A profile on its own (what the Theme tab exports and imports).
let profileOfJson (text: string) : Result<Profile, string> =
    match Json.parse text with
    | Error e -> Error("Not valid JSON: " + e)
    | Ok(JObj _ as j) -> Ok(Newsletter.Profile.ofJson j)
    | Ok _ -> Error "A profile is a JSON object."
