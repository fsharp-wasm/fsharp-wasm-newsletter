/// The designer as a state machine: the model, the messages and a pure `update` that also says which effects the browser must carry
/// out (save, copy).  No DOM here, so every behaviour — undo, selection, import — is tested under .NET.
module Newsletter.Studio

open Newsletter.Model
open Newsletter.Profile
open FSharp.Math
open FSharp.Wasm.Edit
open FSharp.Graphics.Interact

type Panel = PBlock | PDocument | PTheme
type Surface = Preview | Code
type Medium = Email | Web
type Output = OutEmail | OutWeb | OutText | OutJson

type Snapshot = { Doc: Document; Profile: Profile }

/// What a drag carries: a block of the document, or a new block of a kind, from the palette.
type Dragged =
    | DragBlock of int
    | DragNew of Kind

/// A place a drag can drop, as the page measured it: the block's number, x, y, width, height (viewport pixels), whether it takes a drop into it.
type Measured = int * float * float * float * float * bool

type Model =
    { Doc: Document
      Profile: Profile
      Selected: int option
      /// the undo log: every change of the document or the profile is a `Replace` of the whole project; consecutive keystrokes into one field are one step
      History: History<Snapshot>
      /// a block being dragged (and, once the pointer has moved, where it would drop)
      Drag: Session<Dragged>
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
    | DragPress of Dragged * x: float * y: float
    | DragMove of x: float * y: float * slots: Measured list
    | DragRelease of x: float * y: float * slots: Measured list
    | DragCancel
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

let private maxHistory = 100

let init : Model =
    { Doc = Sample.issue; Profile = defaultProfile; Selected = None; History = History.create maxHistory; Drag = Idle
      Panel = PDocument; Surface = Preview; Medium = Email; Mobile = false; Output = OutEmail
      Notice = ""; ImportText = ""; ImportError = "" }

let private snapshot (m: Model) : Snapshot = { Doc = m.Doc; Profile = m.Profile }

/// The project as one item, so that the log of changes is `Replace [0]` of it (FSharp.Wasm.Edit.History folds the keystrokes of one field into one step).
let private projectShape : Shape<Snapshot> = { Kids = (fun _ -> []); WithKids = (fun s _ -> s) }

/// A change to the document or profile: one step of the undo log, unless it continues the edit being typed.
let private commit (key: string) (notice: string) (doc: Document) (profile: Profile) (m: Model) : Model =
    let op = Replace ([ 0 ], snapshot m, { Doc = doc; Profile = profile })
    { m with
        Doc = doc
        Profile = profile
        History = History.record key op m.History
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

let private stillSelected (s: Snapshot) (m: Model) : int option =
    match m.Selected with
    | Some id when s.Doc.Blocks |> List.exists (fun b -> b.Id = id) -> Some id
    | _ -> None

let private undoStep (m: Model) : Model =
    match History.undo projectShape [ snapshot m ] m.History with
    | Some ([ s ], h) -> { m with Doc = s.Doc; Profile = s.Profile; History = h; Notice = "Undone."; Selected = stillSelected s m }
    | _ -> { m with Notice = "Nothing to undo." }

let private redoStep (m: Model) : Model =
    match History.redo projectShape [ snapshot m ] m.History with
    | Some ([ s ], h) -> { m with Doc = s.Doc; Profile = s.Profile; History = h; Notice = "Redone."; Selected = stillSelected s m }
    | _ -> { m with Notice = "Nothing to redo." }

let canUndo (m: Model) : bool = History.canUndo m.History
let canRedo (m: Model) : bool = History.canRedo m.History

/// The places the page measured, as Interact knows them (a list of blocks: each follows the one before downwards, none takes a drop into it).
let private slotsOf (measured: Measured list) : Slot list =
    measured |> List.map (fun (id, x, y, w, h, nests) -> { Id = id; Bounds = Rect.create (float32 x) (float32 y) (float32 w) (float32 h); Flow = Flow.Down; Nests = nests })

let private at (x: float) (y: float) : Vec2 = Vec2.create (float32 x) (float32 y)

/// Is a block being pressed or dragged: the page then reports every move of the pointer.
let dragActive (m: Model) : bool =
    match m.Drag with
    | Idle -> false
    | _ -> true

/// Has the pointer moved far enough for it to be a drag (and not a click): a view then swallows the click that the browser sends after it.
let dragging (m: Model) : bool =
    match m.Drag with
    | Dragging _ -> true
    | _ -> false

/// Is this block the one being dragged (a view dims it).
let beingDragged (m: Model) (id: int) : bool =
    match m.Drag with
    | Dragging (DragBlock dragged, _, _, _) -> dragged = id
    | _ -> false

/// The blocks after dropping `what` at `drop`.  None when that changes nothing (a block onto itself, or right where it already is) or there is no such place.
let private landing (m: Model) (what: Dragged) (drop: Drop) : Block list option =
    let after = (drop.Position = Position.After)
    match what with
    | DragBlock id -> Dnd.dropBlock id drop.Target after m.Doc.Blocks
    | DragNew kind -> Dnd.insertBlock (blank kind m.Doc.NextId) drop.Target after m.Doc.Blocks

/// The line a view draws where the drag would drop (x, y, width, height in viewport pixels).  None when nothing is dragged, the pointer is over no block, or the drop would
/// change nothing (onto the block itself, or where it already is): no mark for a place that does nothing.
let dropMark (m: Model) : (float * float * float * float) option =
    match m.Drag with
    | Dragging (what, _, _, Some drop) ->
        landing m what drop |> Option.map (fun _ -> float drop.Mark.X, float drop.Mark.Y, float drop.Mark.W, float drop.Mark.H)
    | _ -> None

/// What a view shows under the pointer while something is dragged: its name and where the pointer is.
let dragGhost (m: Model) : (string * float * float) option =
    match m.Drag with
    | Dragging (DragBlock id, _, pointer, _) -> Some (describe m id, float pointer.X, float pointer.Y)
    | Dragging (DragNew kind, _, pointer, _) -> Some ("New " + (kindLabel kind).ToLower(), float pointer.X, float pointer.Y)
    | _ -> None

let private step (msg: Msg) (m: Model) : Model * Effect list =
    match msg with
    | Add kind ->
        let doc, id = addBlock kind m.Selected m.Doc
        let m2 = commit "" "" doc m.Profile m
        { m2 with Selected = Some id; Panel = PBlock; Notice = kindLabel kind + " added as " + describe m2 id + "." }, []
    | Select sel ->
        { m with Selected = sel; Panel = (match sel with Some _ -> PBlock | None -> m.Panel); History = { m.History with Open = "" }
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
    | DragPress(what, x, y) -> { m with Drag = Drag.press what (at x y) }, []
    | DragMove(x, y, slots) -> { m with Drag = Drag.move (slotsOf slots) (at x y) m.Drag }, []
    | DragRelease(x, y, slots) ->
        let landed = Drag.move (slotsOf slots) (at x y) m.Drag
        let m1 = { m with Drag = Idle }
        (match Drag.release landed, landed with
         | Some (what, drop), _ ->
            (match landing m what drop, what with
             | Some blocks, DragBlock id ->
                let m2 = commit "" "" { m.Doc with Blocks = blocks } m.Profile m1
                { m2 with Notice = describe m2 id + ": moved by dragging." }, []
             | Some blocks, DragNew kind ->
                let id = m.Doc.NextId
                let m2 = commit "" "" { m.Doc with Blocks = blocks; NextId = id + 1 } m.Profile m1
                { m2 with Selected = Some id; Panel = PBlock; Notice = kindLabel kind + " added as " + describe m2 id + " by dragging." }, []
             | None, _ -> { m1 with Notice = "Dropped where it was." }, [])
         | None, Dragging (DragNew _, _, _, _) -> { m1 with Notice = "Dropped outside the blocks: nothing added." }, []
         | None, _ -> m1, [])
    | DragCancel ->
        let said =
            match m.Drag with
            | Dragging (DragNew _, _, _, _) -> "Adding cancelled."
            | Dragging _ -> "Move cancelled."
            | _ -> m.Notice
        { m with Drag = Idle; Notice = said }, []
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
        | Ok(d, p) -> { m with Doc = d; Profile = p; Selected = None; History = History.create maxHistory }, []
        | Error _ -> m, []
    | Copy -> { m with Notice = "Copying…" }, [ Clipboard(output m) ]
    | Copied ok -> { m with Notice = (if ok then "Copied to the clipboard." else "Copying was blocked by the browser: select the text and copy it by hand.") }, []

/// The next state, and what the browser has to do: keep the project (whenever the document or the profile changed), copy.
let update (msg: Msg) (m: Model) : Model * Effect list =
    let m2, effects = step msg m
    let changed = m2.Doc <> m.Doc || m2.Profile <> m.Profile
    let isRestore = (match msg with Restore _ -> true | _ -> false)
    m2, (if changed && not isRestore then Save(Codec.projectToJson m2.Doc m2.Profile) :: effects else effects)
