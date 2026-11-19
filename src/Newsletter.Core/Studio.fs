/// The designer as a state machine: the model, the messages and a pure `update` that also says which effects the browser must carry
/// out (save, copy).  No DOM here, so every behaviour — undo, selection, import — is tested under .NET.
module Newsletter.Studio

open Newsletter.Model
open Newsletter.Profile

type Panel = PBlock | PDocument | PTheme
type Surface = Preview | Code
type Medium = Email | Web
type Output = OutEmail | OutWeb | OutText | OutJson

type Snapshot = { Doc: Document; Profile: Profile }

type Model =
    { Doc: Document
      Profile: Profile
      Selected: int option
      Past: Snapshot list
      Future: Snapshot list
      /// the edit being typed (block, field): consecutive keystrokes into it are one step of the undo log
      Typing: string
      Panel: Panel
      Surface: Surface
      Medium: Medium
      Mobile: bool
      Output: Output
      /// said to screen readers (a live region) and shown in the status line
      Notice: string
      ImportText: string
      ImportError: string }

type Msg =
    | Add of Kind
    | Select of int option
    | Remove of int
    | Move of id: int * delta: int
    | Duplicate of int
    | EditField of id: int * Field * string
    | EditDoc of DocField * string
    | PickProfile of string
    | EditToken of string * string
    | EditClass of string * string
    | EditStylesheet of string
    | EditScript of string
    | Undo
    | Redo
    | SetPanel of Panel
    | SetSurface of Surface
    | SetMedium of Medium
    | SetMobile of bool
    | SetOutput of Output
    | ImportChanged of string
    | ApplyImport
    | LoadSample
    | NewDocument
    | Restore of string
    | Copy
    | Copied of bool

type Effect =
    /// the project as JSON, to keep in the browser
    | Save of string
    | Clipboard of string

let init : Model =
    { Doc = Sample.issue; Profile = defaultProfile; Selected = None; Past = []; Future = []; Typing = ""
      Panel = PDocument; Surface = Preview; Medium = Email; Mobile = false; Output = OutEmail
      Notice = ""; ImportText = ""; ImportError = "" }

let private maxHistory = 100

let private snapshot (m: Model) : Snapshot = { Doc = m.Doc; Profile = m.Profile }

/// A change to the document or profile: one step of the undo log, unless it continues the edit being typed.
let private commit (key: string) (notice: string) (doc: Document) (profile: Profile) (m: Model) : Model =
    let continues = key <> "" && key = m.Typing
    { m with
        Doc = doc
        Profile = profile
        Past = if continues then m.Past else snapshot m :: (m.Past |> List.truncate (maxHistory - 1))
        Future = []
        Typing = key
        Notice = if notice = "" then m.Notice else notice }

let private block (id: int) (m: Model) : Block option = m.Doc.Blocks |> List.tryFind (fun b -> b.Id = id)

let private describe (m: Model) (id: int) : string =
    match block id m with
    | Some b ->
        let n = (m.Doc.Blocks |> List.findIndex (fun x -> x.Id = id)) + 1
        kindLabel b.Kind + " " + string n + " of " + string m.Doc.Blocks.Length
    | None -> "Block"

/// The text of the code view.
let output (m: Model) : string =
    match m.Output with
    | OutEmail -> Render.email Render.exportOptions m.Profile m.Doc
    | OutWeb -> Render.web Render.exportOptions m.Profile m.Doc
    | OutText -> Render.text m.Doc
    | OutJson -> Codec.projectToJson m.Doc m.Profile

/// The designer ships a local copy of Bootstrap, so the preview does not depend on the network.
let localStylesheet (url: string) : string = if url = bootstrapUrl then "vendor/bootstrap/bootstrap.min.css" else url

/// The page shown in the preview frame.
let previewHtml (m: Model) : string =
    let o : Render.Options = { Preview = true; Selected = (match m.Selected with Some id -> id | None -> -1); Local = localStylesheet }
    match m.Medium with
    | Email -> Render.email o m.Profile m.Doc
    | Web -> Render.web o m.Profile m.Doc

let findings (m: Model) : Lint.Finding list =
    Lint.check m.Profile m.Doc (Render.email Render.exportOptions m.Profile m.Doc).Length

let private undoStep (m: Model) : Model =
    match m.Past with
    | [] -> { m with Notice = "Nothing to undo." }
    | s :: rest ->
        { m with Doc = s.Doc; Profile = s.Profile; Past = rest; Future = snapshot m :: m.Future; Typing = ""; Notice = "Undone."
                 Selected = (match m.Selected with Some id when s.Doc.Blocks |> List.exists (fun b -> b.Id = id) -> Some id | _ -> None) }

let private redoStep (m: Model) : Model =
    match m.Future with
    | [] -> { m with Notice = "Nothing to redo." }
    | s :: rest ->
        { m with Doc = s.Doc; Profile = s.Profile; Future = rest; Past = snapshot m :: m.Past; Typing = ""; Notice = "Redone."
                 Selected = (match m.Selected with Some id when s.Doc.Blocks |> List.exists (fun b -> b.Id = id) -> Some id | _ -> None) }

let private step (msg: Msg) (m: Model) : Model * Effect list =
    match msg with
    | Add kind ->
        let doc, id = addBlock kind m.Selected m.Doc
        let m2 = commit "" "" doc m.Profile m
        { m2 with Selected = Some id; Panel = PBlock; Notice = kindLabel kind + " added as " + describe m2 id + "." }, []
    | Select sel ->
        { m with Selected = sel; Panel = (match sel with Some _ -> PBlock | None -> m.Panel); Typing = ""
                 Notice = (match sel with Some id -> describe m id + " selected." | None -> "") }, []
    | Remove id ->
        let label = describe m id
        let m2 = commit "" "" (removeBlock id m.Doc) m.Profile m
        { m2 with Selected = (if m.Selected = Some id then None else m.Selected); Notice = label + " removed." }, []
    | Move(id, delta) ->
        let doc = moveBlock id delta m.Doc
        if doc = m.Doc then { m with Notice = (if delta < 0 then "Already first." else "Already last.") }, []
        else
            let m2 = commit "" "" doc m.Profile m
            { m2 with Notice = describe m2 id + ": moved " + (if delta < 0 then "up" else "down") + "." }, []
    | Duplicate id ->
        let doc, copy = duplicateBlock id m.Doc
        let m2 = commit "" "" doc m.Profile m
        { m2 with Selected = (match copy with Some c -> Some c | None -> m.Selected); Notice = "Duplicated." }, []
    | EditField(id, f, v) ->
        commit ("f:" + string id + ":" + fieldKey f) "" (updateBlock id (setField f v) m.Doc) m.Profile m, []
    | EditDoc(f, v) -> commit ("d:" + docFieldKey f) "" (setDocField f v m.Doc) m.Profile m, []
    | PickProfile name ->
        match byName name with
        | Some p -> { (commit "" "" m.Doc p m) with Notice = "Profile: " + p.Name + "." }, []
        | None -> m, []
    | EditToken(k, v) -> commit ("t:" + k) "" m.Doc (setToken k v m.Profile) m, []
    | EditClass(k, v) -> commit ("c:" + k) "" m.Doc (setClasses k v m.Profile) m, []
    | EditStylesheet v -> commit "stylesheet" "" m.Doc { m.Profile with Stylesheet = v } m, []
    | EditScript v -> commit "script" "" m.Doc { m.Profile with Script = v } m, []
    | Undo -> undoStep m, []
    | Redo -> redoStep m, []
    | SetPanel p -> { m with Panel = p }, []
    | SetSurface s -> { m with Surface = s }, []
    | SetMedium x -> { m with Medium = x }, []
    | SetMobile b -> { m with Mobile = b }, []
    | SetOutput o -> { m with Output = o }, []
    | ImportChanged t -> { m with ImportText = t; ImportError = "" }, []
    | ApplyImport ->
        // a whole project (document + profile) or a profile on its own
        match Codec.projectOfJson m.ImportText with
        | Ok(d, p) ->
            { (commit "" "" d p m) with Selected = None; ImportText = ""; ImportError = ""; Notice = "Project imported." }, []
        | _ ->
            match Codec.profileOfJson m.ImportText with
            | Ok p when p.Tokens <> [] || p.Classes <> [] || p.Stylesheet <> "" ->
                { (commit "" "" m.Doc p m) with ImportText = ""; ImportError = ""; Notice = "Profile imported: " + p.Name + "." }, []
            | Ok _ -> { m with ImportError = "That JSON has neither a document nor a profile." }, []
            | Error e -> { m with ImportError = e }, []
    | LoadSample -> { (commit "" "" Sample.issue m.Profile m) with Selected = None; Notice = "Sample issue loaded." }, []
    | NewDocument -> { (commit "" "" emptyDocument m.Profile m) with Selected = None; Notice = "New empty issue." }, []
    | Restore json ->
        // what the browser kept from the last visit; an unreadable value is ignored
        match Codec.projectOfJson json with
        | Ok(d, p) -> { m with Doc = d; Profile = p; Selected = None; Past = []; Future = []; Typing = "" }, []
        | Error _ -> m, []
    | Copy -> { m with Notice = "Copying…" }, [ Clipboard(output m) ]
    | Copied ok -> { m with Notice = (if ok then "Copied to the clipboard." else "Copying was blocked by the browser: select the text and copy it by hand.") }, []

/// The next state, and what the browser has to do: keep the project (whenever the document or the profile changed), copy.
let update (msg: Msg) (m: Model) : Model * Effect list =
    let m2, effects = step msg m
    let changed = m2.Doc <> m.Doc || m2.Profile <> m.Profile
    let isRestore = (match msg with Restore _ -> true | _ -> false)
    m2, (if changed && not isRestore then Save(Codec.projectToJson m2.Doc m2.Profile) :: effects else effects)
