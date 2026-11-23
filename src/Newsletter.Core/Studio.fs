/// The designer as a state machine: the model, the messages and a pure `update` that also says which effects the browser must carry
/// out (save, copy).  No DOM here, so every behaviour — undo, selection, import — is tested under .NET.
module Newsletter.Studio

open Newsletter.Model
open Newsletter.Profile
open Newsletter.Projects
open FSharp.Math
open FSharp.Wasm.Data
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
      /// the preview as it was when a text started to be edited in place: the frame is not rendered again while it holds the editor, or it would take it away
      Frozen: string option
      /// where the preview is scrolled, kept so that the next document of the frame is put back there
      Scroll: int
      Panel: Panel
      Surface: Surface
      Medium: Medium
      Mobile: bool
      Output: Output
      /// said to screen readers (a live region) and shown in the status line
      Notice: string
      ImportText: string
      ImportError: string
      /// the projects the browser keeps, and which one is open
      Library: Library
      /// the list of projects is shown, what was typed into its search field, and the project that is being asked about deleting
      ProjectsOpen: bool
      Search: string
      Asking: string option
      /// the last time the host told (milliseconds since the epoch): a pure `update` has no clock
      Clock: float
      /// a frame is asked for to scroll the page for a drag near the window's edge (so that no second one is): when the one before it came (0 for the first)
      Following: float option }

/// What is to be done with a project that the host has read.
type Intent =
    | OpenIt
    | CopyIt

type Msg =
    | Add of Kind
    | Select of int option
    | Remove of int
    | Move of id: int * delta: int
    | DragPress of Dragged * x: float * y: float
    /// the pointer moved: where, the places the page measured, and how tall the window is (the page scrolls for a drag near its top or bottom)
    | DragMove of x: float * y: float * slots: Measured list * viewport: float
    | DragRelease of x: float * y: float * slots: Measured list
    | DragCancel
    /// the animation frame that `NextFrame` asked for: the window's height, the places measured now (after the scroll of the frame before), and the time
    | Frame of viewport: float * slots: Measured list * now: float
    /// what the preview frame said, as the JSON it posted: a click on a block, a text edited in place, its scroll position, that it has loaded
    | FromPreview of string
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
    /// the host has read the browser's storage: the index, the project it names, the single project of versions before the library, and the time
    | Boot of index: string * current: string * legacy: string * now: float
    | ToggleProjects
    | NewProject
    | OpenProject of string
    | DuplicateProject of string
    | AskDelete of string option
    | DeleteProject of string
    | FilterProjects of string
    | ImportAsProject
    /// the host has read a project (None: it is not there or cannot be read)
    | Loaded of id: string * Intent * json: string option
    /// the host has kept a project, at this time
    | Saved of id: string * now: float
    | Copy
    | Copied of bool

type Effect =
    /// the project of this id as JSON, to keep in the browser (the host answers `Saved` with the time)
    | Save of id: string * json: string
    /// the index of the library as JSON
    | SaveIndex of string
    /// read the project of this id and answer `Loaded`
    | Load of id: string * Intent
    /// forget what the browser keeps under this key
    | Erase of string
    | Clipboard of string
    /// something to say to the preview frame, as JSON
    | ToPreview of string
    /// scroll the page by this many pixels (negative: up)
    | ScrollBy of float
    /// answer `Frame` at the next animation frame
    | NextFrame

let private maxHistory = 100

let init : Model =
    { Doc = Sample.issue; Profile = defaultProfile; Selected = None; History = History.create maxHistory; Drag = Idle
      Panel = PDocument; Surface = Preview; Medium = Email; Mobile = false; Output = OutEmail
      Frozen = None; Scroll = 0; Notice = ""; ImportText = ""; ImportError = ""
      Library = Projects.empty; ProjectsOpen = false; Search = ""; Asking = None; Clock = 0.0; Following = None }

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

/// What the preview frame shows: the preview of the document, or, while a text is edited in place, the one that was shown when it started.
let previewDocument (m: Model) : string =
    match m.Frozen with
    | Some html -> html
    | None -> previewHtml m

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

/// How fast the page scrolls for the drag at hand, in pixels per frame of a 60 Hz screen: nothing in the middle of the window, and within a margin of its top or bottom edge up to a speed, faster
/// the closer the pointer is (`Scroll.near`).  The window's height comes from the page, a pure `update` cannot ask; only the vertical axis counts.
let private speedOf (viewport: float) (m: Model) : float =
    match m.Drag with
    | Dragging (_, _, at, _) -> float (Scroll.near (Rect.create 0.0f 0.0f 0.0f (float32 viewport)) 64.0f 18.0f at).Y
    | _ -> 0.0

let private frameMs = 1000.0 / 60.0

/// What a frame scrolls: the speed for the time since the frame before (a screen of 144 Hz scrolls as fast as one of 60, and a frame that came late as far as it was late, but not more than three
/// frames' worth: a tab that was away does not jump).  Whole pixels, and at least one inside the margin: the browser drops a scroll of a fraction, and the strip would stand still.
let private scrollOf (speed: float) (elapsed: float) : float =
    let px = speed * min (3.0 * frameMs) elapsed / frameMs
    if px > 0.0 then ceil px else floor px

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

/// Is a new block of this kind the one being dragged in from the palette?  (A pointer that is captured shows the cursor of the element that captured it, so a view gives
/// that element the closed hand while the drag lasts, as it does the handle of a block.)
let newBeingDragged (m: Model) (kind: Kind) : bool =
    match m.Drag with
    | Dragging (DragNew dragged, _, _, _) -> dragged = kind
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

/// A block number as the preview writes it (an attribute is text).
let private blockOf (j: Json option) : int option =
    match j with
    | Some (JStr s) -> Numbers.parseInt s
    | Some (JNum n) -> Some (int n)
    | _ -> None

// ── the library of projects ──

let private describeProject (m: Model) (id: string) : string =
    match Projects.find id m.Library with
    | Some meta -> meta.Name + " is open."
    | None -> ""

/// The projects the list shows (newest first, those that match the search).
let visibleProjects (m: Model) : Meta list = Projects.visible m.Search m.Library

/// The meta of the project that is open, as the document says it now (the library keeps what a list shows: name, subject, size).
let private mirror (m: Model) : Model =
    match Projects.find m.Library.Current m.Library with
    | Some meta -> { m with Library = Projects.upsert { meta with Name = m.Doc.Name; Subject = m.Doc.Subject; Blocks = m.Doc.Blocks.Length } m.Library }
    | None -> m

let private indexOf (m: Model) : Effect = SaveIndex(Projects.libraryToJson m.Library)

/// A project of its own for `doc` and `profile` (opened if `openIt`): added to the library, and kept.
let private addProject (doc: Document) (profile: Profile) (openIt: bool) (notice: string) (m: Model) : Model * Effect list =
    let id = Projects.nextId m.Library.Items
    let meta = Projects.metaOf id m.Clock m.Clock doc
    let library = { Items = meta :: m.Library.Items; Current = (if openIt then id else m.Library.Current) }
    let m2 = { m with Library = library; Notice = notice; ProjectsOpen = (if openIt then false else m.ProjectsOpen) }
    let kept = [ Save(id, Codec.projectToJson doc profile); indexOf m2 ]
    if openIt then { m2 with Doc = doc; Profile = profile; Selected = None; History = History.create maxHistory; Panel = PDocument }, kept
    else m2, kept

/// The project that was read, as the model: the document and the profile, a fresh log of changes, nothing selected.
let private open' (id: string) (doc: Document) (profile: Profile) (m: Model) : Model =
    { m with Doc = doc; Profile = profile; Selected = None; History = History.create maxHistory; Panel = PDocument
             Library = { m.Library with Current = id }; ProjectsOpen = false; Asking = None
             Notice = "Opened " + doc.Name + "." }

/// What the browser kept, as the model: the library and the open project, or (a first visit, or the single project of the versions before) a library of one.
let private boot (index: string) (current: string) (legacy: string) (now: float) (m: Model) : Model * Effect list =
    let m = { m with Clock = now }
    match Projects.libraryOfJson index with
    | Some library when not library.Items.IsEmpty ->
        let wanted = (if (Projects.find library.Current library).IsSome then library.Current else (Projects.byRecent library.Items).Head.Id)
        let m1 = { m with Library = { library with Current = wanted } }
        (match Codec.projectOfJson current with
         | Ok(d, p) when wanted = library.Current -> { (open' wanted d p m1) with Notice = "" }, []
         // the project it was left on is gone: open the newest
         | _ -> { m1 with Library = { library with Current = "" } }, [ Load(wanted, OpenIt) ])
    | _ ->
        // the single project of the versions before the library, or the sample of a first visit: the library of one
        let doc, profile, migrated =
            match Codec.projectOfJson legacy with
            | Ok(d, p) -> d, p, true
            | Error _ -> m.Doc, m.Profile, false
        let id = Projects.nextId []
        let library = { Items = [ Projects.metaOf id now now doc ]; Current = id }
        let m2 = { m with Doc = doc; Profile = profile; Library = library; History = History.create maxHistory }
        m2, [ Save(id, Codec.projectToJson doc profile); SaveIndex(Projects.libraryToJson library) ] @ (if migrated then [ Erase Projects.legacyKey ] else [])

let rec private step (msg: Msg) (m: Model) : Model * Effect list =
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
    | DragMove(x, y, slots, viewport) ->
        let m1 = { m with Drag = Drag.move (slotsOf slots) (at x y) m.Drag }
        // one frame at a time asks the next while the pointer rests near the edge: a pointer that rests sends no events
        if m1.Following.IsSome || speedOf viewport m1 = 0.0 then m1, [] else { m1 with Following = Some 0.0 }, [ NextFrame ]
    | Frame(viewport, slots, now) ->
        // the places have moved with the last scroll: the drop is read again, for the pointer where it rests
        let m1 = (match m.Drag with Dragging (_, _, pointer, _) -> { m with Drag = Drag.move (slotsOf slots) pointer m.Drag } | _ -> m)
        let speed = speedOf viewport m1
        let elapsed = (match m.Following with Some last when last > 0.0 -> now - last | _ -> frameMs)
        if speed = 0.0 then { m1 with Following = None }, [] else { m1 with Following = Some now }, [ ScrollBy (scrollOf speed elapsed); NextFrame ]
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
    | FromPreview json ->
        (match Json.tryParse json with
         | None -> m, []
         | Some j ->
            (match Json.field "edit" j, Json.field "editing" j, blockOf (Json.field "block" j) with
             // typing in the editor over a block: the text of that block
             | Some e, _, _ ->
                (match blockOf (Json.field "block" e) with
                 | Some id when (match block id m with Some b -> hasInlineText b.Kind | None -> false) ->
                    commit ("f:" + string id + ":" + fieldKey FText) "" (updateBlock id (setField FText (Json.stringOr "value" "" e)) m.Doc) m.Profile m, []
                 | _ -> m, [])
             // the editor is open (the preview stays as it is) or closed (it follows the document again)
             | None, Some (JBool true), _ -> { m with Frozen = Some (previewHtml m) }, []
             | None, Some _, _ -> { m with Frozen = None }, []
             // a click on a block selects it
             | None, None, Some id -> (step (Select (Some id)) m)
             | None, None, None ->
                (match Json.field "scroll" j, Json.field "ready" j with
                 | Some (JNum n), _ -> { m with Scroll = int n }, []
                 // the frame has loaded a document: put it back where it was
                 | _, Some (JBool true) -> m, [ ToPreview ("{\"scroll\":" + string m.Scroll + "}") ]
                 | _ -> m, [])))
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
    | Boot(index, current, legacy, now) -> boot index current legacy now m
    | ToggleProjects -> { m with ProjectsOpen = not m.ProjectsOpen; Asking = None; Notice = (if m.ProjectsOpen then "Projects closed." else "Projects: " + string m.Library.Items.Length + ".") }, []
    | NewProject ->
        let name = Projects.numbered "Untitled issue" m.Library.Items
        addProject { emptyDocument with Name = name } m.Profile true ("New newsletter: " + name + ".") m
    | OpenProject id ->
        if id = m.Library.Current then { m with ProjectsOpen = false; Notice = describeProject m id }, []
        else { m with Notice = "Opening…" }, [ Load(id, OpenIt) ]
    | DuplicateProject id ->
        if id = m.Library.Current then
            addProject { m.Doc with Name = Projects.copyName m.Doc.Name m.Library.Items } m.Profile false ("Duplicated as " + Projects.copyName m.Doc.Name m.Library.Items + ".") m
        else m, [ Load(id, CopyIt) ]
    | AskDelete id -> { m with Asking = id }, []
    | DeleteProject id ->
        let name = (match Projects.find id m.Library with Some meta -> meta.Name | None -> "Project")
        let library = Projects.remove id m.Library
        let m1 = { m with Library = library; Asking = None; Notice = name + " deleted." }
        if id <> m.Library.Current then m1, [ Erase(Projects.projectKey id); indexOf m1 ]
        else
            // the open one: nothing is open until the newest of the rest has been read (the document in memory is the deleted one's, so it must not be kept under another id)
            match Projects.byRecent library.Items with
            | next :: _ ->
                let m2 = { m1 with Library = { library with Current = "" } }
                m2, [ Erase(Projects.projectKey id); indexOf m2; Load(next.Id, OpenIt) ]
            | [] ->
                let doc = { emptyDocument with Name = "Untitled issue" }
                let fresh, fx = addProject doc m.Profile true (name + " deleted. A new empty newsletter is open.") m1
                fresh, Erase(Projects.projectKey id) :: fx
    | FilterProjects text -> { m with Search = text }, []
    | ImportAsProject ->
        match Codec.projectOfJson m.ImportText with
        | Ok(d, p) ->
            let doc = { d with Name = Projects.numbered d.Name m.Library.Items }
            let m2, fx = addProject doc p true ("Imported as a new newsletter: " + doc.Name + ".") { m with ImportText = ""; ImportError = "" }
            m2, fx
        | Error e -> { m with ImportError = e }, []
    | Loaded(id, intent, json) ->
        (match json |> Option.map Codec.projectOfJson with
         | Some(Ok(d, p)) ->
            (match intent with
             | OpenIt ->
                let m2 = open' id d p m
                m2, [ indexOf m2 ]
             | CopyIt -> addProject { d with Name = Projects.copyName d.Name m.Library.Items } p false ("Duplicated as " + Projects.copyName d.Name m.Library.Items + ".") m)
         | _ -> { m with Notice = "That project could not be read." }, [])
    | Saved(id, now) ->
        let m1 = { m with Clock = now }
        (match Projects.find id m1.Library with
         | Some meta ->
            let meta2 = { meta with Modified = now; Created = (if meta.Created = 0.0 then now else meta.Created) }
            let m2 = { m1 with Library = Projects.upsert meta2 m1.Library }
            m2, [ indexOf m2 ]
         | None -> m1, [])
    | Copy -> { m with Notice = "Copying…" }, [ Clipboard(output m) ]
    | Copied ok -> { m with Notice = (if ok then "Copied to the clipboard." else "Copying was blocked by the browser: select the text and copy it by hand.") }, []

/// The next state, and what the browser has to do: keep the project (whenever the document or the profile changed), copy.
let update (msg: Msg) (m: Model) : Model * Effect list =
    let m2, effects = step msg m
    // a preview that holds an editor is kept as it is while the frame talks (typing, scrolling); anything else lets it follow the document again
    // (`Saved` comes after every keystroke that is kept: it is not a change of what the preview shows)
    let m2 = (match msg with FromPreview _ | Saved _ -> m2 | _ -> { m2 with Frozen = None })
    let changed = m2.Doc <> m.Doc || m2.Profile <> m.Profile
    // what was read from the browser (or a project that was just made, which `step` keeps itself) is not kept again
    let fromStorage = (match msg with Restore _ | Boot _ | Loaded _ | NewProject | ImportAsProject | DeleteProject _ -> true | _ -> false)
    if changed && not fromStorage then
        let m3 = mirror m2
        m3, Save(m3.Library.Current, Codec.projectToJson m3.Doc m3.Profile) :: effects
    else m2, effects
