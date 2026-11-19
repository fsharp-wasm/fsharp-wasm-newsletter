/// A small JSON reader and writer, so that documents and themes can be saved, loaded and exchanged.  It uses only what
/// fsharp-wasm compiles (no System.Text.Json), and the same code runs under .NET in the tests.
module Newsletter.Json

open System.Text

type Json =
    | JNull
    | JBool of bool
    | JNum of float
    | JStr of string
    | JArr of Json list
    | JObj of (string * Json) list

let private hex (n: int) : string =
    let digits = "0123456789abcdef"
    let at (shift: int) = digits.Substring((n >>> shift) &&& 15, 1)
    at 12 + at 8 + at 4 + at 0

let private quote (s: string) : string =
    let sb = StringBuilder()
    sb.Append('"') |> ignore
    for c in s do
        if c = '"' then sb.Append("\\\"") |> ignore
        elif c = '\\' then sb.Append("\\\\") |> ignore
        elif c = '\n' then sb.Append("\\n") |> ignore
        elif c = '\r' then sb.Append("\\r") |> ignore
        elif c = '\t' then sb.Append("\\t") |> ignore
        elif int c < 32 then sb.Append("\\u").Append(hex (int c)) |> ignore
        else sb.Append(c) |> ignore
    sb.Append('"') |> ignore
    sb.ToString()

let private number (f: float) : string =
    if f = System.Math.Floor f && abs f < 1e15 then string (int64 f) else string f

/// The JSON text of a value; `pretty` indents by two spaces.
let stringify (pretty: bool) (j: Json) : string =
    let sb = StringBuilder()
    let nl (depth: int) =
        if pretty then
            sb.Append('\n') |> ignore
            for _ in 1 .. depth do sb.Append("  ") |> ignore
    let rec go (depth: int) (v: Json) : unit =
        match v with
        | JNull -> sb.Append("null") |> ignore
        | JBool b -> sb.Append(if b then "true" else "false") |> ignore
        | JNum f -> sb.Append(number f) |> ignore
        | JStr s -> sb.Append(quote s) |> ignore
        | JArr [] -> sb.Append("[]") |> ignore
        | JArr items ->
            sb.Append('[') |> ignore
            let mutable first = true
            for item in items do
                if not first then sb.Append(',') |> ignore
                first <- false
                nl (depth + 1)
                go (depth + 1) item
            nl depth
            sb.Append(']') |> ignore
        | JObj [] -> sb.Append("{}") |> ignore
        | JObj fields ->
            sb.Append('{') |> ignore
            let mutable first = true
            for (k, item) in fields do
                if not first then sb.Append(',') |> ignore
                first <- false
                nl (depth + 1)
                sb.Append(quote k).Append(if pretty then ": " else ":") |> ignore
                go (depth + 1) item
            nl depth
            sb.Append('}') |> ignore
    go 0 j
    sb.ToString()

/// Parses JSON text.  The error names the position of the first problem.  (No exceptions: the WasmGC target carries them
/// without a payload, so the parser records the first error, jumps to the end of the text, and every loop stops.)
let parse (text: string) : Result<Json, string> =
    let n = text.Length
    let pos = ref 0
    let error = ref ""
    let fail (msg: string) : unit =
        if error.Value = "" then error.Value <- msg + " at position " + string pos.Value
        pos.Value <- n
    let ok () = error.Value = ""
    let skip () =
        while pos.Value < n && (text.[pos.Value] = ' ' || text.[pos.Value] = '\n' || text.[pos.Value] = '\r' || text.[pos.Value] = '\t') do
            pos.Value <- pos.Value + 1
    let expect (c: char) =
        if pos.Value < n && text.[pos.Value] = c then pos.Value <- pos.Value + 1
        else fail ("expected '" + string c + "'")
    let word (w: string) =
        if pos.Value + w.Length <= n && text.Substring(pos.Value, w.Length) = w then pos.Value <- pos.Value + w.Length
        else fail "unexpected character"
    let hexVal (c: char) : int =
        if c >= '0' && c <= '9' then int c - int '0'
        elif c >= 'a' && c <= 'f' then int c - int 'a' + 10
        elif c >= 'A' && c <= 'F' then int c - int 'A' + 10
        else (fail "bad \\u escape"; 0)
    let str () : string =
        expect '"'
        let sb = StringBuilder()
        let mutable closed = false
        while not closed && ok () do
            if pos.Value >= n then fail "unterminated string"
            else
                let c = text.[pos.Value]
                pos.Value <- pos.Value + 1
                if c = '"' then closed <- true
                elif c = '\\' then
                    if pos.Value >= n then fail "unterminated string"
                    else
                        let e = text.[pos.Value]
                        pos.Value <- pos.Value + 1
                        if e = 'n' then sb.Append('\n') |> ignore
                        elif e = 'r' then sb.Append('\r') |> ignore
                        elif e = 't' then sb.Append('\t') |> ignore
                        elif e = 'b' then sb.Append('\b') |> ignore
                        elif e = 'f' then sb.Append('\012') |> ignore
                        elif e = '/' || e = '\\' || e = '"' then sb.Append(e) |> ignore
                        elif e = 'u' then
                            if pos.Value + 4 > n then fail "bad \\u escape"
                            else
                                let code = (hexVal text.[pos.Value] <<< 12) ||| (hexVal text.[pos.Value + 1] <<< 8) ||| (hexVal text.[pos.Value + 2] <<< 4) ||| hexVal text.[pos.Value + 3]
                                pos.Value <- pos.Value + 4
                                sb.Append(char code) |> ignore
                        else fail "bad escape"
                else sb.Append(c) |> ignore
        sb.ToString()
    let digits () : bool =
        let s = pos.Value
        while pos.Value < n && text.[pos.Value] >= '0' && text.[pos.Value] <= '9' do pos.Value <- pos.Value + 1
        pos.Value > s
    let num () : float =
        let start = pos.Value
        if pos.Value < n && text.[pos.Value] = '-' then pos.Value <- pos.Value + 1
        let intStart = pos.Value
        if not (digits ()) then fail "bad number"
        let mutable v = 0.0
        for i in intStart .. pos.Value - 1 do
            v <- v * 10.0 + float (int text.[i] - int '0')
        if ok () && pos.Value < n && text.[pos.Value] = '.' then
            pos.Value <- pos.Value + 1
            let fracStart = pos.Value
            if not (digits ()) then fail "bad number"
            let mutable scale = 1.0
            for i in fracStart .. pos.Value - 1 do
                scale <- scale / 10.0
                v <- v + float (int text.[i] - int '0') * scale
        if ok () && pos.Value < n && (text.[pos.Value] = 'e' || text.[pos.Value] = 'E') then
            pos.Value <- pos.Value + 1
            let mutable negative = false
            if pos.Value < n && (text.[pos.Value] = '+' || text.[pos.Value] = '-') then
                negative <- (text.[pos.Value] = '-')
                pos.Value <- pos.Value + 1
            let expStart = pos.Value
            if not (digits ()) then fail "bad number"
            let mutable e = 0
            for i in expStart .. pos.Value - 1 do
                e <- min 400 (e * 10 + (int text.[i] - int '0'))
            if negative then (for _ in 1 .. e do v <- v / 10.0) else (for _ in 1 .. e do v <- v * 10.0)
        if text.[start] = '-' then -v else v
    let rec value () : Json =
        skip ()
        if pos.Value >= n then (fail "unexpected end"; JNull)
        else
            let c = text.[pos.Value]
            if c = '{' then
                pos.Value <- pos.Value + 1
                skip ()
                if pos.Value < n && text.[pos.Value] = '}' then
                    pos.Value <- pos.Value + 1
                    JObj []
                else
                    let fields = ResizeArray<string * Json>()
                    let mutable more = true
                    while more && ok () do
                        skip ()
                        let k = str ()
                        skip ()
                        expect ':'
                        let v = value ()
                        fields.Add((k, v))
                        skip ()
                        if pos.Value < n && text.[pos.Value] = ',' then pos.Value <- pos.Value + 1
                        else more <- false
                    expect '}'
                    JObj(List.ofSeq fields)
            elif c = '[' then
                pos.Value <- pos.Value + 1
                skip ()
                if pos.Value < n && text.[pos.Value] = ']' then
                    pos.Value <- pos.Value + 1
                    JArr []
                else
                    let items = ResizeArray<Json>()
                    let mutable more = true
                    while more && ok () do
                        items.Add(value ())
                        skip ()
                        if pos.Value < n && text.[pos.Value] = ',' then pos.Value <- pos.Value + 1
                        else more <- false
                    expect ']'
                    JArr(List.ofSeq items)
            elif c = '"' then JStr(str ())
            elif c = 't' then (word "true"; JBool true)
            elif c = 'f' then (word "false"; JBool false)
            elif c = 'n' then (word "null"; JNull)
            elif c = '-' || (c >= '0' && c <= '9') then JNum(num ())
            else (fail "unexpected character"; JNull)
    let v = value ()
    skip ()
    if ok () && pos.Value < n then fail "unexpected text after the value"
    if ok () then Ok v else Error error.Value

// ── reading a value: every accessor has a default, so a document with a missing field still loads ──
let field (name: string) (j: Json) : Json option =
    match j with
    | JObj fields -> fields |> List.tryFind (fun (k, _) -> k = name) |> Option.map snd
    | _ -> None

let str (name: string) (fallback: string) (j: Json) : string =
    match field name j with Some(JStr s) -> s | _ -> fallback

let num (name: string) (fallback: float) (j: Json) : float =
    match field name j with Some(JNum f) -> f | _ -> fallback

let int' (name: string) (fallback: int) (j: Json) : int =
    match field name j with Some(JNum f) -> int f | _ -> fallback

let bool' (name: string) (fallback: bool) (j: Json) : bool =
    match field name j with Some(JBool b) -> b | _ -> fallback

let list (name: string) (j: Json) : Json list =
    match field name j with Some(JArr items) -> items | _ -> []
