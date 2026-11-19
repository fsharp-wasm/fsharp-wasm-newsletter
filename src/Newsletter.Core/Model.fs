/// The document: a list of blocks, each one of a few kinds, edited field by field.  One record serves every kind (a kind uses
/// only some fields; `fieldsOf` says which), so that the inspector, the codec and the undo log need no case per kind.
module Newsletter.Model

type Kind = Heading | Paragraph | Image | Button | Quote | Divider | Spacer | Columns | Footer
type Align = Left | Center | Right
type Variant = Primary | Secondary | Outline

type Block =
    { Id: int
      Kind: Kind
      /// heading, paragraph, quote, button label, left column, footer text
      Text: string
      /// right column, quote author, footer address
      Text2: string
      /// image source, button link, footer unsubscribe link
      Url: string
      /// image alternative text, the link of an image
      Alt: string
      Link: string
      Align: Align
      /// heading level 1..3
      Level: int
      /// image width in percent, spacer height in pixels
      Size: int
      Variant: Variant }

type Document =
    { Name: string
      Subject: string
      Preheader: string
      Lang: string
      Blocks: Block list
      NextId: int }

// ── names: one table per enumeration; the codec, the inspector and the renderers all read these ──
let kinds : (Kind * string * string) list =
    [ Heading, "heading", "Heading"; Paragraph, "paragraph", "Text"; Image, "image", "Image"; Button, "button", "Button"
      Quote, "quote", "Quote"; Divider, "divider", "Divider"; Spacer, "spacer", "Spacer"; Columns, "columns", "Two columns"
      Footer, "footer", "Footer" ]
let aligns : (Align * string) list = [ Left, "left"; Center, "center"; Right, "right" ]
let variants : (Variant * string) list = [ Primary, "primary"; Secondary, "secondary"; Outline, "outline" ]

let kindKey (k: Kind) : string = kinds |> List.find (fun (x, _, _) -> x = k) |> fun (_, key, _) -> key
let kindLabel (k: Kind) : string = kinds |> List.find (fun (x, _, _) -> x = k) |> fun (_, _, label) -> label
let kindOfKey (s: string) : Kind option = kinds |> List.tryFind (fun (_, key, _) -> key = s) |> Option.map (fun (k, _, _) -> k)
let alignKey (a: Align) : string = aligns |> List.find (fun (x, _) -> x = a) |> snd
let alignOfKey (s: string) : Align = aligns |> List.tryFind (fun (_, key) -> key = s) |> Option.map fst |> Option.defaultValue Left
let variantKey (v: Variant) : string = variants |> List.find (fun (x, _) -> x = v) |> snd
let variantOfKey (s: string) : Variant = variants |> List.tryFind (fun (_, key) -> key = s) |> Option.map fst |> Option.defaultValue Primary

/// Whole decimal number, or None.  (No System.Int32.TryParse: one small function instead of a dependency.)
let parseInt (s: string) : int option =
    let t = s.Trim()
    let negative = t.StartsWith "-"
    let start = if negative then 1 else 0
    if t.Length - start = 0 || t.Length - start > 9 then None
    else
        let mutable v = 0
        let mutable ok = true
        for i in start .. t.Length - 1 do
            let c = t.[i]
            if c >= '0' && c <= '9' then v <- v * 10 + (int c - int '0') else ok <- false
        if ok then Some(if negative then -v else v) else None

let clamp (lo: int) (hi: int) (v: int) : int = max lo (min hi v)

// ── fields ──
type Field = FText | FText2 | FUrl | FAlt | FLink | FAlign | FLevel | FSize | FVariant

let fieldKey (f: Field) : string =
    match f with
    | FText -> "text" | FText2 -> "text2" | FUrl -> "url" | FAlt -> "alt" | FLink -> "link" | FAlign -> "align" | FLevel -> "level" | FSize -> "size" | FVariant -> "variant"

type Input =
    | Line
    | Area
    | Choice of (string * string) list
    | Range of lo: int * hi: int * suffix: string

type FieldSpec = { Field: Field; Label: string; Input: Input; Help: string }

let private spec f label input help = { Field = f; Label = label; Input = input; Help = help }
let private alignChoice = Choice(aligns |> List.map (fun (a, k) -> k, (match a with Left -> "Left" | Center -> "Centre" | Right -> "Right")))

/// What the inspector offers for each kind, in order.
let fieldsOf (k: Kind) : FieldSpec list =
    match k with
    | Heading -> [ spec FText "Text" Line ""; spec FLevel "Level" (Range(1, 3, "")) "1 is the title of the issue; use 2 and 3 for sections."; spec FAlign "Alignment" alignChoice "" ]
    | Paragraph -> [ spec FText "Text" Area "Blank line = new paragraph. **bold**, _italic_, [label](https://link)."; spec FAlign "Alignment" alignChoice "" ]
    | Image ->
        [ spec FUrl "Image address" Line "https://… (host the image; email clients do not take uploads)"
          spec FAlt "Alternative text" Line "What the image says, for people who do not see it. Required."
          spec FLink "Link (optional)" Line ""
          spec FSize "Width" (Range(10, 100, "%")) ""
          spec FAlign "Alignment" alignChoice "" ]
    | Button ->
        [ spec FText "Label" Line "Say what happens: “Read the story”, not “Click here”."
          spec FUrl "Link" Line "https://…"
          spec FVariant "Style" (Choice [ "primary", "Primary"; "secondary", "Secondary"; "outline", "Outline" ]) ""
          spec FAlign "Alignment" alignChoice "" ]
    | Quote -> [ spec FText "Quote" Area ""; spec FText2 "Author" Line "" ]
    | Divider -> []
    | Spacer -> [ spec FSize "Height" (Range(4, 120, "px")) "" ]
    | Columns -> [ spec FText "Left column" Area "Same markup as text."; spec FText2 "Right column" Area "" ]
    | Footer ->
        [ spec FText "Reason for receiving it" Area ""
          spec FText2 "Sender address" Line "A postal address is required by law in many countries."
          spec FUrl "Unsubscribe link" Line "Your sending service fills {{unsubscribe_url}} in." ]

let getField (f: Field) (b: Block) : string =
    match f with
    | FText -> b.Text | FText2 -> b.Text2 | FUrl -> b.Url | FAlt -> b.Alt | FLink -> b.Link
    | FAlign -> alignKey b.Align | FLevel -> string b.Level | FSize -> string b.Size | FVariant -> variantKey b.Variant

/// Sets a field; numbers are clamped to what the kind allows, an unparsable number leaves the block as it was.
let setField (f: Field) (v: string) (b: Block) : Block =
    let number lo hi apply = match parseInt v with Some n -> apply (clamp lo hi n) | None -> b
    match f with
    | FText -> { b with Text = v } | FText2 -> { b with Text2 = v } | FUrl -> { b with Url = v }
    | FAlt -> { b with Alt = v } | FLink -> { b with Link = v }
    | FAlign -> { b with Align = alignOfKey v } | FVariant -> { b with Variant = variantOfKey v }
    | FLevel -> number 1 3 (fun n -> { b with Level = n })
    | FSize -> if b.Kind = Spacer then number 4 120 (fun n -> { b with Size = n }) else number 10 100 (fun n -> { b with Size = n })

/// A new block with sensible content, so that adding one shows something.
let blank (kind: Kind) (id: int) : Block =
    let b = { Id = id; Kind = kind; Text = ""; Text2 = ""; Url = ""; Alt = ""; Link = ""; Align = Left; Level = 2; Size = 100; Variant = Primary }
    match kind with
    | Heading -> { b with Text = "A new heading" }
    | Paragraph -> { b with Text = "Write something worth reading. Use **bold**, _italic_ and [links](https://example.com)." }
    | Image -> { b with Url = "https://placehold.co/600x300/png"; Alt = "Describe the image"; Align = Center }
    | Button -> { b with Text = "Read more"; Url = "https://example.com"; Align = Center }
    | Quote -> { b with Text = "A sentence that deserves to stand alone."; Text2 = "Someone" }
    | Divider -> b
    | Spacer -> { b with Size = 24 }
    | Columns -> { b with Text = "Left column."; Text2 = "Right column." }
    | Footer -> { b with Text = "You are receiving this because you subscribed."; Text2 = "Example Ltd · 1 Main Street · 12345 Town"; Url = "{{unsubscribe_url}}"; Align = Center }

/// One line that says what a block holds: the outline of the designer shows it next to the kind.
let summary (b: Block) : string =
    let clip (s: string) =
        let one = s.Replace("\n", " ").Trim()
        if one.Length > 44 then one.Substring(0, 43) + "…" else one
    match b.Kind with
    | Heading | Paragraph | Button | Quote | Columns | Footer -> clip b.Text
    | Image -> if b.Alt.Trim() = "" then "no alternative text" else clip b.Alt
    | Spacer -> string b.Size + " px"
    | Divider -> ""

// ── the document ──
type DocField = DName | DSubject | DPreheader | DLang

let docFieldKey (f: DocField) : string =
    match f with DName -> "name" | DSubject -> "subject" | DPreheader -> "preheader" | DLang -> "lang"

let getDocField (f: DocField) (d: Document) : string =
    match f with DName -> d.Name | DSubject -> d.Subject | DPreheader -> d.Preheader | DLang -> d.Lang

let setDocField (f: DocField) (v: string) (d: Document) : Document =
    match f with
    | DName -> { d with Name = v } | DSubject -> { d with Subject = v } | DPreheader -> { d with Preheader = v } | DLang -> { d with Lang = v }

let emptyDocument : Document = { Name = "Untitled issue"; Subject = ""; Preheader = ""; Lang = "en"; Blocks = []; NextId = 1 }

// ── block operations ──
let addBlock (kind: Kind) (after: int option) (d: Document) : Document * int =
    let b = blank kind d.NextId
    let blocks =
        match after with
        | Some id when d.Blocks |> List.exists (fun x -> x.Id = id) ->
            d.Blocks |> List.collect (fun x -> if x.Id = id then [ x; b ] else [ x ])
        | _ -> d.Blocks @ [ b ]
    { d with Blocks = blocks; NextId = d.NextId + 1 }, b.Id

let removeBlock (id: int) (d: Document) : Document = { d with Blocks = d.Blocks |> List.filter (fun b -> b.Id <> id) }

let updateBlock (id: int) (f: Block -> Block) (d: Document) : Document =
    { d with Blocks = d.Blocks |> List.map (fun b -> if b.Id = id then f b else b) }

/// Moves a block by `delta` places (negative = up), stopping at the ends.
let moveBlock (id: int) (delta: int) (d: Document) : Document =
    match d.Blocks |> List.tryFindIndex (fun b -> b.Id = id) with
    | None -> d
    | Some i ->
        let j = clamp 0 (d.Blocks.Length - 1) (i + delta)
        if i = j then d
        else
            let arr = List.toArray d.Blocks
            let moved = arr.[i]
            let rest = d.Blocks |> List.filter (fun b -> b.Id <> id)
            { d with Blocks = (List.take j rest) @ [ moved ] @ (List.skip j rest) }

let duplicateBlock (id: int) (d: Document) : Document * int option =
    match d.Blocks |> List.tryFind (fun b -> b.Id = id) with
    | None -> d, None
    | Some b ->
        let copy = { b with Id = d.NextId }
        { d with Blocks = d.Blocks |> List.collect (fun x -> if x.Id = id then [ x; copy ] else [ x ]); NextId = d.NextId + 1 }, Some copy.Id
