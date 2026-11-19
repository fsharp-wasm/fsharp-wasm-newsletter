namespace FSharp.Wasm.Ui

open System
open System.Text

// ───────────────────────────── values and declarations ─────────────────────────────

type Length =
    | Px of float
    | Em of float
    | Rem of float
    | Pct of float
    | Vw of float
    | Vh of float
    | Zero
    | Auto
    | Calc of string

type Value =
    | VLength of Length
    | VColor of Color
    | VNumber of float
    | VKeyword of string
    /// written as it is: font stacks, urls
    | VText of string
    /// space separated
    | VList of Value list
    /// comma separated
    | VComma of Value list

/// One declaration.  The constructors in `Style` take typed arguments; the record is what the renderer, the inliner and the merge read.
type Decl = { Name: string; Value: Value; Important: bool }

[<RequireQualifiedAccess>]
type Display = | Block | Inline | InlineBlock | Table | TableCell | TableRow | NoDisplay | Flex | Grid
[<RequireQualifiedAccess>]
type TextAlign = | Left | Center | Right | Justify | Inherit
[<RequireQualifiedAccess>]
type VerticalAlign = | Top | Middle | Bottom | Baseline
[<RequireQualifiedAccess>]
type FontWeight = | Normal | Bold | Weight of int
[<RequireQualifiedAccess>]
type BorderStyle = | Solid | Dashed | Dotted | NoBorder
[<RequireQualifiedAccess>]
type TextDecoration = | NoDecoration | Underline
[<RequireQualifiedAccess>]
type Float = | Left | Right | NoFloat
[<RequireQualifiedAccess>]
type Visibility = | Visible | Hidden

// ───────────────────────────── selectors and rules ─────────────────────────────

/// One compound selector: `td.columns.first[align=center]:not(.x)`.  `Attrs` are (name, operator, value); the operator is "" for presence.
type Simple =
    { Tag: string option
      Id: string option
      Classes: string list
      Attrs: (string * string * string) list
      Pseudos: string list
      Not: Simple list }

type Selector =
    | Compound of Simple
    | Descendant of Selector * Selector
    | Child of Selector * Selector

type MediaCond =
    | MaxWidth of Length
    | MinWidth of Length
    | MediaRaw of string

type Rule =
    | Styled of Selector list * Decl list
    | Media of MediaCond * Rule list
    | Layer of string * Rule list
    /// a container query, the condition as CSS writes it: `(min-width: 400px)`
    | Container of string * Rule list
    | Comment of string

module Selector =
    let emptySimple : Simple = { Tag = None; Id = None; Classes = []; Attrs = []; Pseudos = []; Not = [] }

    let private isIdent (c: char) : bool = Char.IsLetterOrDigit c || c = '-' || c = '_'

    /// Parses `table.body .columns > th.large-6:not([class*=offset])`.  A malformed selector is a programmer error and fails at once.
    let parse (text: string) : Selector =
        let s = text.Trim()
        let n = s.Length
        let pos = ref 0
        let fail (msg: string) : unit = failwith ("selector '" + text + "': " + msg)
        let ident () : string =
            let start = pos.Value
            while pos.Value < n && isIdent s.[pos.Value] do pos.Value <- pos.Value + 1
            s.Substring(start, pos.Value - start)
        let rec compound (insideNot: bool) : Simple =
            let mutable c = emptySimple
            let mutable go = true
            while go && pos.Value < n do
                let ch = s.[pos.Value]
                if ch = '.' then
                    pos.Value <- pos.Value + 1
                    c <- { c with Classes = c.Classes @ [ ident () ] }
                elif ch = '#' then
                    pos.Value <- pos.Value + 1
                    c <- { c with Id = Some(ident ()) }
                elif ch = '[' then
                    pos.Value <- pos.Value + 1
                    let name = ident ()
                    let opStart = pos.Value
                    while pos.Value < n && s.[pos.Value] <> ']' && s.[pos.Value] <> '=' do pos.Value <- pos.Value + 1
                    let mutable op = ""
                    let mutable value = ""
                    if pos.Value < n && s.[pos.Value] = '=' then
                        op <- s.Substring(opStart, pos.Value - opStart + 1)
                        pos.Value <- pos.Value + 1
                        let vStart = pos.Value
                        while pos.Value < n && s.[pos.Value] <> ']' do pos.Value <- pos.Value + 1
                        let raw = s.Substring(vStart, pos.Value - vStart).Trim()
                        value <- (if raw.Length >= 2 && (raw.[0] = '"' || raw.[0] = '\'') then raw.Substring(1, raw.Length - 2) else raw)
                    if pos.Value < n && s.[pos.Value] = ']' then pos.Value <- pos.Value + 1 else fail "missing ]"
                    c <- { c with Attrs = c.Attrs @ [ name, op, value ] }
                elif ch = ':' then
                    pos.Value <- pos.Value + 1
                    let name = ident ()
                    if name = "not" && pos.Value < n && s.[pos.Value] = '(' then
                        pos.Value <- pos.Value + 1
                        let inner = compound true
                        if pos.Value < n && s.[pos.Value] = ')' then pos.Value <- pos.Value + 1 else fail "missing )"
                        c <- { c with Not = c.Not @ [ inner ] }
                    else c <- { c with Pseudos = c.Pseudos @ [ name ] }
                elif ch = '*' then pos.Value <- pos.Value + 1
                elif isIdent ch && c.Tag.IsNone && c.Classes.IsEmpty then c <- { c with Tag = Some(ident ()) }
                elif insideNot && ch = ')' then go <- false
                elif ch = ' ' || ch = '>' then go <- false
                else
                    fail ("unexpected '" + string ch + "'")
                    go <- false
            c
        let skipSpaces () = while pos.Value < n && s.[pos.Value] = ' ' do pos.Value <- pos.Value + 1
        let mutable result = Compound(compound false)
        skipSpaces ()
        while pos.Value < n do
            let child = s.[pos.Value] = '>'
            if child then pos.Value <- pos.Value + 1
            skipSpaces ()
            let next = Compound(compound false)
            result <- (if child then Child(result, next) else Descendant(result, next))
            skipSpaces ()
        result

    let rec private simpleCss (c: Simple) : string =
        let tag = defaultArg c.Tag ""
        let id = match c.Id with Some i -> "#" + i | None -> ""
        let classes = c.Classes |> List.map (fun x -> "." + x) |> String.concat ""
        let attrs = c.Attrs |> List.map (fun (n, op, v) -> if op = "" then "[" + n + "]" else "[" + n + op + "\"" + v + "\"]") |> String.concat ""
        let pseudos = c.Pseudos |> List.map (fun p -> ":" + p) |> String.concat ""
        let nots = c.Not |> List.map (fun x -> ":not(" + simpleCss x + ")") |> String.concat ""
        let body = tag + id + classes + attrs + pseudos + nots
        if body = "" then "*" else body

    let rec css (sel: Selector) : string =
        match sel with
        | Compound c -> simpleCss c
        | Descendant(a, b) -> css a + " " + css b
        | Child(a, b) -> css a + " > " + css b

    let rec private simpleSpecificity (c: Simple) : int =
        (if c.Id.IsSome then 10000 else 0)
        + 100 * (c.Classes.Length + c.Attrs.Length + c.Pseudos.Length + (c.Not |> List.sumBy (fun x -> simpleSpecificity x / 100)))
        + (if c.Tag.IsSome then 1 else 0)

    /// ids * 10 000 + classes, attributes and pseudo-classes * 100 + tags.
    let rec specificity (sel: Selector) : int =
        match sel with
        | Compound c -> simpleSpecificity c
        | Descendant(a, b) | Child(a, b) -> specificity a + specificity b

    /// An inliner can only apply a rule that does not depend on state (`:hover`, `:visited`).
    let rec isInlinable (sel: Selector) : bool =
        match sel with
        | Compound c -> c.Pseudos.IsEmpty
        | Descendant(a, b) | Child(a, b) -> isInlinable a && isInlinable b

// ───────────────────────────── the typed constructors ─────────────────────────────

module Style =
    let decl (name: string) (value: Value) : Decl = { Name = name; Value = value; Important = false }
    let important (d: Decl) : Decl = { d with Important = true }
    /// The escape hatch for what has no constructor yet.
    let raw (name: string) (value: string) : Decl = decl name (VText value)

    let private len (name: string) (l: Length) = decl name (VLength l)
    let private kw (name: string) (k: string) = decl name (VKeyword k)

    let width (l: Length) = len "width" l
    let height (l: Length) = len "height" l
    let minWidth (l: Length) = len "min-width" l
    let maxWidth (l: Length) = len "max-width" l
    let margin (l: Length) = len "margin" l
    let marginBottom (l: Length) = len "margin-bottom" l
    let marginTop (l: Length) = len "margin-top" l
    let marginLeft (l: Length) = len "margin-left" l
    let marginRight (l: Length) = len "margin-right" l
    let padding (l: Length) = len "padding" l
    let paddingTop (l: Length) = len "padding-top" l
    let paddingRight (l: Length) = len "padding-right" l
    let paddingBottom (l: Length) = len "padding-bottom" l
    let paddingLeft (l: Length) = len "padding-left" l
    /// vertical, horizontal
    let paddingXY (v: Length) (h: Length) = decl "padding" (VList [ VLength v; VLength h ])
    let padding4 (t: Length) (r: Length) (b: Length) (l: Length) = decl "padding" (VList [ VLength t; VLength r; VLength b; VLength l ])
    let margin4 (t: Length) (r: Length) (b: Length) (l: Length) = decl "margin" (VList [ VLength t; VLength r; VLength b; VLength l ])
    let color (c: Color) = decl "color" (VColor c)
    let background (c: Color) = decl "background" (VColor c)
    let backgroundColor (c: Color) = decl "background-color" (VColor c)
    let fontFamily (stack: string) = decl "font-family" (VText stack)
    let fontSize (l: Length) = len "font-size" l
    let lineHeight (factor: float) = decl "line-height" (VNumber factor)
    let lineHeightLen (l: Length) = len "line-height" l
    let fontWeight (w: FontWeight) =
        match w with
        | FontWeight.Normal -> kw "font-weight" "normal"
        | FontWeight.Bold -> kw "font-weight" "bold"
        | FontWeight.Weight n -> decl "font-weight" (VNumber(float n))
    let textAlign (a: TextAlign) =
        kw "text-align" (match a with TextAlign.Left -> "left" | TextAlign.Center -> "center" | TextAlign.Right -> "right" | TextAlign.Justify -> "justify" | TextAlign.Inherit -> "inherit")
    let verticalAlign (a: VerticalAlign) =
        kw "vertical-align" (match a with VerticalAlign.Top -> "top" | VerticalAlign.Middle -> "middle" | VerticalAlign.Bottom -> "bottom" | VerticalAlign.Baseline -> "baseline")
    let display (d: Display) =
        kw "display" (match d with Display.Block -> "block" | Display.Inline -> "inline" | Display.InlineBlock -> "inline-block" | Display.Table -> "table"
                                   | Display.TableCell -> "table-cell" | Display.TableRow -> "table-row" | Display.NoDisplay -> "none" | Display.Flex -> "flex" | Display.Grid -> "grid")
    let float' (f: Float) = kw "float" (match f with Float.Left -> "left" | Float.Right -> "right" | Float.NoFloat -> "none")
    let visibility (v: Visibility) = kw "visibility" (match v with Visibility.Visible -> "visible" | Visibility.Hidden -> "hidden")
    let textDecoration (d: TextDecoration) = kw "text-decoration" (match d with TextDecoration.NoDecoration -> "none" | TextDecoration.Underline -> "underline")
    let borderStyleWord (s: BorderStyle) = match s with BorderStyle.Solid -> "solid" | BorderStyle.Dashed -> "dashed" | BorderStyle.Dotted -> "dotted" | BorderStyle.NoBorder -> "none"
    /// `border: 2px solid #0d6efd`
    let border (w: Length) (style: BorderStyle) (c: Color) = decl "border" (VList [ VLength w; VKeyword(borderStyleWord style); VColor c ])
    let borderBottom (w: Length) (style: BorderStyle) (c: Color) = decl "border-bottom" (VList [ VLength w; VKeyword(borderStyleWord style); VColor c ])
    let borderNone = kw "border" "none"
    let borderRadius (l: Length) = len "border-radius" l
    let borderCollapse (collapse: bool) = kw "border-collapse" (if collapse then "collapse" else "separate")
    let boxSizing (borderBox: bool) = kw "box-sizing" (if borderBox then "border-box" else "content-box")
    let opacity (o: float) = decl "opacity" (VNumber o)
    let customProperty (name: string) (value: Value) = decl ("--" + name) value

    // ── lowering and merging ──
    let lengthCss (l: Length) : string =
        match l with
        | Px v -> Num.fmt v + "px"
        | Em v -> Num.fmt v + "em"
        | Rem v -> Num.fmt v + "rem"
        | Pct v -> Num.fmt v + "%"
        | Vw v -> Num.fmt v + "vw"
        | Vh v -> Num.fmt v + "vh"
        | Zero -> "0"
        | Auto -> "auto"
        | Calc e -> "calc(" + e + ")"

    let rec valueCss (t: Target) (v: Value) : string =
        match v with
        | VLength l -> lengthCss l
        | VColor c -> Color.css t c
        | VNumber n -> Num.fmt n
        | VKeyword k -> k
        | VText s -> s
        | VList vs -> vs |> List.map (valueCss t) |> String.concat " "
        | VComma vs -> vs |> List.map (valueCss t) |> String.concat ", "

    let declCss (t: Target) (d: Decl) : string =
        d.Name + ": " + valueCss t d.Value + (if d.Important then " !important" else "")

    /// A later declaration of a property replaces an earlier one, unless the earlier is `!important` and the later is not.
    let merge (decls: Decl list) : Decl list =
        decls
        |> List.fold (fun (acc: Decl list) d ->
            match acc |> List.tryFind (fun x -> x.Name = d.Name) with
            | Some existing when existing.Important && not d.Important -> acc
            | Some _ -> (acc |> List.filter (fun x -> x.Name <> d.Name)) @ [ d ]
            | None -> acc @ [ d ]) []

    /// The value of a `style` attribute: `padding: 16px; color: #0a0a0a`.
    let inlineStyle (t: Target) (decls: Decl list) : string =
        merge decls |> List.map (declCss t) |> String.concat "; "

    // ── stylesheets ──
    let rule (selectors: string list) (decls: Decl list) : Rule = Styled(selectors |> List.map Selector.parse, decls)
    let mediaMaxWidth (l: Length) (rules: Rule list) : Rule = Media(MaxWidth l, rules)

    let private mediaCss (c: MediaCond) : string =
        match c with
        | MaxWidth l -> "only screen and (max-width: " + lengthCss l + ")"
        | MinWidth l -> "only screen and (min-width: " + lengthCss l + ")"
        | MediaRaw s -> s

    let rec private ruleCss (t: Target) (sb: StringBuilder) (indent: string) (rule: Rule) : unit =
        match rule with
        | Comment text -> sb.Append(indent).Append("/* ").Append(text).Append(" */\n") |> ignore
        | Styled(sels, decls) ->
            let head = sels |> List.map (fun s -> indent + Selector.css s) |> String.concat ",\n"
            sb.Append(head).Append(" {\n") |> ignore
            for d in decls do sb.Append(indent).Append("  ").Append(declCss t d).Append(";\n") |> ignore
            sb.Append(indent).Append("}\n") |> ignore
        | Media(cond, rules) -> block t sb indent ("@media " + mediaCss cond) rules
        | Layer(name, rules) -> block t sb indent ("@layer " + name) rules
        | Container(cond, rules) -> block t sb indent ("@container " + cond) rules
    and private block (t: Target) (sb: StringBuilder) (indent: string) (head: string) (rules: Rule list) : unit =
        sb.Append(indent).Append(head).Append(" {\n") |> ignore
        for r in rules do ruleCss t sb (indent + "  ") r
        sb.Append(indent).Append("}\n") |> ignore

    /// The stylesheet as text.
    let render (t: Target) (rules: Rule list) : string =
        let sb = StringBuilder()
        for r in rules do ruleCss t sb "" r
        sb.ToString()
