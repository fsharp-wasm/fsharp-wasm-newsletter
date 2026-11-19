namespace FSharp.Wasm.Ui

/// Applies a stylesheet to a tree the way an email build inlines it (Foundation for Emails uses a Node tool for this): every element
/// gets the declarations of the rules that match it, in the order of specificity and source, and its own styles last.  What cannot be
/// inlined — media queries, `:hover` — is returned to go into a `<style>` block.  Nothing is inlined that nothing matches, so a rule
/// that matches no element costs nothing.
module Inline =
    type private Info = { Tag: string; Id: string; Classes: string list; Attrs: (string * string) list }

    let private info (tag: string) (attrs: (string * string) list) : Info =
        let attr name = attrs |> List.tryFind (fun (k, _) -> k = name) |> Option.map snd |> Option.defaultValue ""
        { Tag = tag
          Id = attr "id"
          Classes = (attr "class").Split([| ' ' |], System.StringSplitOptions.RemoveEmptyEntries) |> Array.toList
          Attrs = attrs }

    let private attrMatches (i: Info) (name: string, op: string, value: string) : bool =
        match i.Attrs |> List.tryFind (fun (k, _) -> k = name) with
        | None -> false
        | Some(_, actual) ->
            if op = "" then true
            elif op = "=" then actual = value
            elif op = "*=" then value <> "" && actual.Contains value
            elif op = "^=" then actual.StartsWith value
            elif op = "$=" then actual.EndsWith value
            elif op = "~=" then (actual.Split([| ' ' |], System.StringSplitOptions.RemoveEmptyEntries) |> Array.contains value)
            else false

    let rec private simpleMatches (c: Simple) (i: Info) : bool =
        (match c.Tag with Some t -> t = i.Tag | None -> true)
        && (match c.Id with Some d -> d = i.Id | None -> true)
        && (c.Classes |> List.forall (fun k -> i.Classes |> List.contains k))
        && (c.Attrs |> List.forall (attrMatches i))
        && (c.Not |> List.forall (fun x -> not (simpleMatches x i)))

    /// `ancestors`: the parents of the element, nearest first.
    let rec private matches (sel: Selector) (i: Info) (ancestors: Info list) : bool =
        match sel with
        | Compound c -> simpleMatches c i
        | Child(l, r) ->
            matches r i ancestors
            && (match ancestors with
                | parent :: rest -> matches l parent rest
                | [] -> false)
        | Descendant(l, r) ->
            matches r i ancestors
            && (let rec up (anc: Info list) =
                    match anc with
                    | [] -> false
                    | a :: rest -> matches l a rest || up rest
                up ancestors)

    type private Entry = { Sel: Selector; Decls: Decl list; Spec: int; Order: int }

    /// The tree with the matching declarations written into its elements, and the rules that must stay in a stylesheet.
    let apply (rules: Rule list) (root: Node) : Node * Rule list =
        let entries = ResizeArray<Entry>()
        let kept = ResizeArray<Rule>()
        let mutable order = 0
        for r in rules do
            match r with
            | Styled(sels, decls) ->
                let inl, stay = sels |> List.partition Selector.isInlinable
                for s in inl do
                    entries.Add { Sel = s; Decls = decls; Spec = Selector.specificity s; Order = order }
                    order <- order + 1
                if not stay.IsEmpty then kept.Add(Styled(stay, decls))
            | other -> kept.Add other
        let sorted = entries |> Seq.toList |> List.sortBy (fun e -> e.Spec * 100000 + e.Order)
        let rec walk (ancestors: Info list) (n: Node) : Node =
            match n with
            | El(tag, attrs, own, kids) ->
                let me = info tag attrs
                let fromRules = sorted |> List.filter (fun e -> matches e.Sel me ancestors) |> List.collect (fun e -> e.Decls)
                let style = if fromRules.IsEmpty then own else Style.merge (fromRules @ own)
                El(tag, attrs, style, kids |> List.map (walk (me :: ancestors)))
            | Fragment kids -> Fragment(kids |> List.map (walk ancestors))
            | other -> other
        walk [] root, List.ofSeq kept
