/// The library of projects the designer keeps in the browser: an index of what exists (name, subject, size, when it was last saved) and each project under a key of its own, so that
/// a list of a hundred newsletters is one small read and opening one is one more.  Pure: no clock and no storage here (the designer's host reads and writes, and tells the time as a
/// message), so everything below is tested under .NET and as WebAssembly GC.
module Newsletter.Projects

open FSharp.Wasm.Data
open Newsletter.Model

/// What the library knows of a project without opening it.  `Name` and `Subject` are the document's own (the name is the one field "only for you"), kept here so that a list needs no reads.
type Meta =
    { Id: string
      Name: string
      Subject: string
      Blocks: int
      /// milliseconds since the epoch (UTC); 0 when it is not known (a project that was migrated before the clock was heard)
      Created: float
      Modified: float }

/// The projects, and the id of the one that is open ("" when none is: the host has not read it yet, or it was just deleted).
type Library = { Items: Meta list; Current: string }

let empty : Library = { Items = []; Current = "" }

let indexKey : string = "fsharp-wasm-newsletter/projects"
let projectKey (id: string) : string = "fsharp-wasm-newsletter/project/" + id
/// The one project that the versions before the library kept, under a key of its own.
let legacyKey : string = "fsharp-wasm-newsletter/project"

// ── ids and names ──

/// "p<n>", n one above the largest number in use: no clock, and an id is not given twice in a library.
let nextId (items: Meta list) : string =
    let number (m: Meta) = if m.Id.StartsWith "p" then defaultArg (parseInt (m.Id.Substring 1)) 0 else 0
    "p" + string (1 + (items |> List.fold (fun biggest m -> max biggest (number m)) 0))

/// `name`, or when the library has a project of that name already, "name 2", "name 3", …
let numbered (name: string) (items: Meta list) : string =
    let taken (n: string) = items |> List.exists (fun m -> m.Name = n)
    let rec pick (i: int) : string = if taken (name + " " + string i) then pick (i + 1) else name + " " + string i
    if taken name then pick 2 else name

/// The name of a copy: "name (copy)", "name (copy 2)", …
let copyName (name: string) (items: Meta list) : string =
    let taken (n: string) = items |> List.exists (fun m -> m.Name = n)
    let rec pick (i: int) : string =
        let candidate = name + " (copy " + string i + ")"
        if taken candidate then pick (i + 1) else candidate
    if taken (name + " (copy)") then pick 2 else name + " (copy)"

// ── the list ──

let find (id: string) (library: Library) : Meta option = library.Items |> List.tryFind (fun m -> m.Id = id)

/// Replace the project of that id, or add it (newest first).
let upsert (meta: Meta) (library: Library) : Library =
    if library.Items |> List.exists (fun m -> m.Id = meta.Id) then { library with Items = library.Items |> List.map (fun m -> if m.Id = meta.Id then meta else m) }
    else { library with Items = meta :: library.Items }

let remove (id: string) (library: Library) : Library = { library with Items = library.Items |> List.filter (fun m -> m.Id <> id) }

/// The meta of a document, as of `modified`.
let metaOf (id: string) (created: float) (modified: float) (d: Document) : Meta =
    { Id = id; Name = d.Name; Subject = d.Subject; Blocks = d.Blocks.Length; Created = created; Modified = modified }

/// Newest first, the order the library shows.
let byRecent (items: Meta list) : Meta list = items |> List.sortByDescending (fun m -> m.Modified)

/// Does the project match what was typed into the search field: a part of its name or subject, whatever the case (nothing typed matches all)?
let matches (search: string) (m: Meta) : bool =
    let wanted = search.Trim().ToLower()
    wanted = "" || m.Name.ToLower().Contains wanted || m.Subject.ToLower().Contains wanted

/// What the library shows: the projects that match, newest first.
let visible (search: string) (library: Library) : Meta list = library.Items |> List.filter (matches search) |> byRecent

// ── the index as JSON ──

let private two (n: int) : string = if n < 10 then "0" + string n else string n

/// "2026-11-22 18:20" (UTC) for milliseconds since the epoch, by the calendar arithmetic of days since 1970 (no date library: the module runs as WebAssembly GC); "" when the time is not known.
let stamp (ms: float) : string =
    if ms <= 0.0 then ""
    else
        let days = int (floor (ms / 86400000.0))
        let ofDay = int (floor ((ms - float days * 86400000.0) / 60000.0))
        let z = days + 719468
        let era = (if z >= 0 then z else z - 146096) / 146097
        let doe = z - era * 146097
        let yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365
        let doy = doe - (365 * yoe + yoe / 4 - yoe / 100)
        let mp = (5 * doy + 2) / 153
        let day = doy - (153 * mp + 2) / 5 + 1
        let month = if mp < 10 then mp + 3 else mp - 9
        let year = yoe + era * 400 + (if month <= 2 then 1 else 0)
        string year + "-" + two month + "-" + two day + " " + two (ofDay / 60) + ":" + two (ofDay % 60)

let private metaToJson (m: Meta) : Json =
    JObj [ "id", JStr m.Id; "name", JStr m.Name; "subject", JStr m.Subject; "blocks", JNum(float m.Blocks); "created", JNum m.Created; "modified", JNum m.Modified ]

let private numberOr (key: string) (fallback: float) (j: Json) : float =
    match Json.asNumber (Json.field key j) with
    | Some n -> n
    | None -> fallback

let private metaOfJson (j: Json) : Meta option =
    match Json.asString (Json.field "id" j) with
    | Some id when id <> "" ->
        Some { Id = id
               Name = Json.stringOr "name" "" j
               Subject = Json.stringOr "subject" "" j
               Blocks = int (numberOr "blocks" 0.0 j)
               Created = numberOr "created" 0.0 j
               Modified = numberOr "modified" 0.0 j }
    | _ -> None

let libraryToJson (library: Library) : string =
    Json.stringify (JObj [ "current", JStr library.Current; "projects", JArr(library.Items |> List.map metaToJson) ])

/// The library from its JSON; None when the text is not one (an entry that is not a project is skipped, the rest stands).
let libraryOfJson (text: string) : Library option =
    match Json.tryParse text with
    | None -> None
    | Some j ->
        match Json.field "projects" j with
        | Some(JArr entries) -> Some { Items = entries |> List.choose metaOfJson; Current = Json.stringOr "current" "" j }
        | _ -> None
