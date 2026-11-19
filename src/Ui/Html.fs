namespace FSharp.Wasm.Ui

open System.Text

/// A document tree that is pure data: it renders to a string (an email, a page made on a server or in a Worker), and its elements
/// carry their styles as typed declarations until the target decides how they are written.
type Node =
    | El of tag: string * attrs: (string * string) list * style: Decl list * children: Node list
    | Text of string
    /// markup that is trusted as it is (conditional comments, an already escaped fragment)
    | Raw of string
    | Fragment of Node list

module Html =
    let escape (s: string) : string =
        let sb = StringBuilder(s.Length + 8)
        for c in s do
            if c = '&' then sb.Append("&amp;") |> ignore
            elif c = '<' then sb.Append("&lt;") |> ignore
            elif c = '>' then sb.Append("&gt;") |> ignore
            elif c = '"' then sb.Append("&quot;") |> ignore
            elif c = '\'' then sb.Append("&#39;") |> ignore
            else sb.Append(c) |> ignore
        sb.ToString()

    // ── building ──
    let el (tag: string) (attrs: (string * string) list) (children: Node list) : Node = El(tag, attrs, [], children)
    let styled (tag: string) (attrs: (string * string) list) (style: Decl list) (children: Node list) : Node = El(tag, attrs, style, children)
    let text (s: string) : Node = Text s
    let raw (s: string) : Node = Raw s
    let cls (names: string) : string * string = "class", names
    /// Adds attributes to an element (the last value of a name wins; classes are joined).
    let withAttrs (extra: (string * string) list) (n: Node) : Node =
        match n with
        | El(tag, attrs, style, kids) ->
            let merged =
                extra |> List.fold (fun (acc: (string * string) list) (k, v) ->
                    match acc |> List.tryFind (fun (n, _) -> n = k) with
                    | Some(_, old) when k = "class" -> (acc |> List.filter (fun (n, _) -> n <> k)) @ [ k, old + " " + v ]
                    | Some _ -> (acc |> List.filter (fun (n, _) -> n <> k)) @ [ k, v ]
                    | None -> acc @ [ k, v ]) attrs
            El(tag, merged, style, kids)
        | other -> other

    // ── rendering ──
    let private voidTags = [ "area"; "base"; "br"; "col"; "embed"; "hr"; "img"; "input"; "link"; "meta"; "source"; "wbr" ]

    let rec private write (t: Target) (sb: StringBuilder) (n: Node) : unit =
        match n with
        | Text s -> sb.Append(escape s) |> ignore
        | Raw s -> sb.Append(s) |> ignore
        | Fragment kids -> for k in kids do write t sb k
        | El(tag, attrs, style, kids) ->
            sb.Append('<').Append(tag) |> ignore
            for (k, v) in attrs do
                if k <> "style" then
                    if v = "" then sb.Append(' ').Append(k) |> ignore
                    else sb.Append(' ').Append(k).Append("=\"").Append(escape v).Append('"') |> ignore
            let own = attrs |> List.tryFind (fun (k, _) -> k = "style") |> Option.map snd
            let fromDecls = if style.IsEmpty then "" else Style.inlineStyle t style
            // the element's own `style` attribute is the most specific: it goes last
            let styleText = match own with Some o when fromDecls <> "" -> fromDecls + "; " + o | Some o -> o | None -> fromDecls
            if styleText <> "" then sb.Append(" style=\"").Append(escape styleText).Append('"') |> ignore
            sb.Append('>') |> ignore
            if not (voidTags |> List.contains tag) then
                for k in kids do write t sb k
                sb.Append("</").Append(tag).Append('>') |> ignore

    let render (t: Target) (n: Node) : string =
        let sb = StringBuilder()
        write t sb n
        sb.ToString()
