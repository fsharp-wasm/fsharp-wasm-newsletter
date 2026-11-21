/// The designer's interface: Bootstrap 5.3 markup built with the Browser.Html DSL.  The chrome is Bootstrap's own look on
/// purpose; what the profile configures is the newsletter that is designed, not this tool.
module Newsletter.Designer.View

open Browser.Html
open Browser.Html.Elements
open Newsletter
open Newsletter.Model
open Newsletter.Profile
open Newsletter.Studio

// ── small pieces ──
let private attrIf (cond: bool) (k: string) (v: string) : HtmlItem = if cond then attr k v else attr "" ""

/// A toggle in a button group: pressed = this one is on.
let private toggle (text: string) (on: bool) (act: unit -> unit) : Node =
    button {
        type' "button"
        class' (if on then "btn btn-primary" else "btn btn-outline-dark")
        attr "aria-pressed" (if on then "true" else "false")
        onClick act
        text
    }

let private group (name: string) (items: Node list) : Node =
    div {
        class' "btn-group btn-group-sm"
        role "group"
        ariaLabel name
        for it in items do it
    }

let private iconButton (name: string) (text: string) (enabled: bool) (act: unit -> unit) : Node =
    button {
        type' "button"
        class' "btn btn-sm btn-outline-dark"
        ariaLabel name
        attr "title" name
        disabled (not enabled)
        onClick act
        text
    }

let private fieldId (prefix: string) (key: string) : string = prefix + "-" + key

let private help (id: string) (text: string) : Node =
    if text = "" then Empty else div { id' id; class' "form-text"; text }

// ── a text-like control for one field ──
let private textInput (id: string) (value: string) (invalid: bool) (describedBy: string) (onEdit: string -> unit) : Node =
    input {
        id' id
        type' "text"
        class' (if invalid then "form-control form-control-sm is-invalid" else "form-control form-control-sm")
        value' value
        attrIf (describedBy <> "") "aria-describedby" describedBy
        attrIf invalid "aria-invalid" "true"
        onInput onEdit
    }

let private control (id: string) (spec: FieldSpec) (value: string) (onEdit: string -> unit) : Node =
    let described = if spec.Help = "" then "" else id + "-help"
    match spec.Input with
    | Line -> textInput id value false described onEdit
    | Area ->
        textarea {
            id' id
            class' "form-control form-control-sm"
            attr "rows" "5"
            value' value
            attrIf (described <> "") "aria-describedby" described
            onInput onEdit
        }
    | Choice options ->
        select {
            id' id
            class' "form-select form-select-sm"
            value' value
            attrIf (described <> "") "aria-describedby" described
            onChange onEdit
            for (key, text) in options do
                option { value' key; text }
        }
    | Range(lo, hi, suffix) ->
        div {
            class' "d-flex align-items-center gap-2"
            input {
                id' id
                type' "range"
                class' "form-range flex-grow-1"
                attr "min" (string lo)
                attr "max" (string hi)
                value' value
                attrIf (described <> "") "aria-describedby" described
                onInput onEdit
            }
            span { class' "small text-nowrap"; attr "aria-hidden" "true"; value + suffix }
        }

let private fieldRow (prefix: string) (spec: FieldSpec) (value: string) (onEdit: string -> unit) : Node =
    let id = fieldId prefix (fieldKey spec.Field)
    div {
        class' "mb-3"
        label { attr "for" id; class' "form-label small fw-semibold mb-1"; spec.Label }
        control id spec value onEdit
        help (id + "-help") spec.Help
    }

// ── left: the blocks ──
let private palette (dispatch: Msg -> unit) : Node =
    div {
        class' "d-grid gap-1"
        for (kind, _, name) in kinds do
            button {
                type' "button"
                class' "btn btn-sm btn-outline-primary text-start"
                attr "data-add" (kindKey kind)
                onClick (fun () -> dispatch (Add kind))
                "+ " + name
            }
    }

/// The handle that drags a block: a press on it follows the pointer (the page captures it), the page reports the places of all blocks with every move, and Escape
/// cancels.  A pointer is not everybody's way: the Move buttons beside it do the same with the keyboard.
let private dragHandle (m: Model) (dispatch: Msg -> unit) (name: string) (id: int) : Node =
    button {
        type' "button"
        class' "btn btn-sm btn-flat text-body-secondary px-1"
        attr "style" "touch-action:none;cursor:grab;user-select:none"       // none: the page must not pan or select text under a drag
        ariaLabel ("Drag " + name + " to another place, or use its Move buttons")
        attr "title" ("Drag " + name)
        onPointerDown (fun x y -> dispatch (DragPress(DragBlock id, x, y)))
        onPointerMove (fun x y -> if dragActive m then dispatch (DragMove(x, y, Browser.Events.measureSlots ())))
        onPointerUp (fun x y -> if dragActive m then dispatch (DragRelease(x, y, Browser.Events.measureSlots ())))
        onPointerCancel (fun () -> dispatch DragCancel)
        onKeyDown (fun key -> if key = "Escape" then dispatch DragCancel)
        "⠿"
    }

let private outlineItem (m: Model) (dispatch: Msg -> unit) (index: int) (b: Block) : Node =
    let selected = (m.Selected = Some b.Id)
    let last = m.Doc.Blocks.Length - 1
    let name = kindLabel b.Kind + " " + string (index + 1)
    let dragged = beingDragged m b.Id
    li {
        key' (string b.Id)
        data' "slot" (string b.Id)
        class' ((if selected then "list-group-item p-1 border-primary bg-primary-subtle" else "list-group-item p-1") + (if dragged then " opacity-50" else ""))
        div {
            class' "d-flex align-items-center gap-1"
            dragHandle m dispatch name b.Id
            button {
                type' "button"
                class' "btn btn-sm btn-flat text-start flex-grow-1 text-truncate"
                data' "select" (string b.Id)
                attrIf selected "aria-current" "true"
                onClick (fun () -> dispatch (Select(Some b.Id)))
                span { class' "fw-semibold"; kindLabel b.Kind }
                span { class' "text-body-secondary ms-2"; Markup.inlineText (summary b) }
            }
            iconButton ("Move " + name + " up") "↑" (index > 0) (fun () -> dispatch (Move(b.Id, -1)))
            iconButton ("Move " + name + " down") "↓" (index < last) (fun () -> dispatch (Move(b.Id, 1)))
            iconButton ("Duplicate " + name) "⧉" true (fun () -> dispatch (Duplicate b.Id))
            iconButton ("Delete " + name) "✕" true (fun () -> dispatch (Remove b.Id))
        }
    }

let private outline (m: Model) (dispatch: Msg -> unit) : Node =
    if m.Doc.Blocks.IsEmpty then
        p "No blocks yet: add one above."
    else
        ol {
            class' "list-group list-group-flush gap-1 p-0 m-0"
            attr "aria-label" "Blocks of the newsletter, in order"
            for (i, b) in List.indexed m.Doc.Blocks do
                outlineItem m dispatch i b
        }

let private left (m: Model) (dispatch: Msg -> unit) : Node =
    nav {
        class' "d-flex flex-column gap-3"
        ariaLabel "Blocks"
        section {
            h2 "Add a block"
            palette dispatch
        }
        section {
            h2 "Structure"
            outline m dispatch
        }
    }

// ── right: inspector ──
let private findingsOf (m: Model) (block: int option) : Lint.Finding list =
    findings m |> List.filter (fun f -> f.Block = block)

let private alertFor (f: Lint.Finding) : Node =
    div {
        class' (if f.Severity = Lint.Error then "alert alert-danger py-1 px-2 small mb-2" else "alert alert-warning py-1 px-2 small mb-2")
        (if f.Severity = Lint.Error then "Error: " else "Warning: ") + f.Message
    }

let private blockPanel (m: Model) (dispatch: Msg -> unit) : Node =
    match m.Selected |> Option.bind (fun id -> m.Doc.Blocks |> List.tryFind (fun b -> b.Id = id)) with
    | None -> p "Select a block in the structure or in the preview to edit it."
    | Some b ->
        div {
            h3 (kindLabel b.Kind)
            for f in findingsOf m (Some b.Id) do alertFor f
            for spec in fieldsOf b.Kind do
                fieldRow ("b" + string b.Id) spec (getField spec.Field b) (fun v -> dispatch (EditField(b.Id, spec.Field, v)))
            if (fieldsOf b.Kind).IsEmpty then p "This block has nothing to set."
        }

let private docField (m: Model) (dispatch: Msg -> unit) (f: DocField) (title: string) (helpText: string) : Node =
    let id = "doc-" + docFieldKey f
    div {
        class' "mb-3"
        label { attr "for" id; class' "form-label small fw-semibold mb-1"; title }
        textInput id (getDocField f m.Doc) false (if helpText = "" then "" else id + "-help") (fun v -> dispatch (EditDoc(f, v)))
        help (id + "-help") helpText
    }

let private documentPanel (m: Model) (dispatch: Msg -> unit) : Node =
    div {
        h3 "Newsletter"
        for f in findingsOf m None do alertFor f
        docField m dispatch DName "Name (only for you)" ""
        docField m dispatch DSubject "Subject line" "What the inbox shows first. Aim for 60 characters or fewer."
        docField m dispatch DPreheader "Preheader" "The grey preview text after the subject."
        docField m dispatch DLang "Language" "A language code such as en or de; screen readers use it."
        div {
            class' "d-flex gap-2 mb-3"
            button { type' "button"; class' "btn btn-sm btn-outline-dark"; onClick (fun () -> dispatch LoadSample); "Load sample" }
            button { type' "button"; class' "btn btn-sm btn-outline-danger"; onClick (fun () -> dispatch NewDocument); "New empty issue" }
        }
        h3 "Import"
        div {
            class' "mb-2"
            label { attr "for" "import-text"; class' "form-label small fw-semibold mb-1"; "Project or profile (JSON)" }
            textarea {
                id' "import-text"
                class' (if m.ImportError = "" then "form-control form-control-sm font-monospace" else "form-control form-control-sm font-monospace is-invalid")
                attr "rows" "4"
                value' m.ImportText
                attrIf (m.ImportError <> "") "aria-describedby" "import-error"
                onInput (fun v -> dispatch (ImportChanged v))
            }
            if m.ImportError <> "" then div { id' "import-error"; class' "invalid-feedback d-block"; m.ImportError }
        }
        button { type' "button"; class' "btn btn-sm btn-primary"; disabled (m.ImportText.Trim() = ""); onClick (fun () -> dispatch ApplyImport); "Import" }
    }

// ── the theme ──
let private tokenRow (m: Model) (dispatch: Msg -> unit) (key: string, title: string, kind: TokenKind) : Node =
    let id = "token-" + key
    let value = token key m.Profile
    let bad = (kind = ColorToken && not (Color.isColor value))
    div {
        class' "mb-2"
        label { attr "for" id; class' "form-label small mb-0"; title }
        div {
            class' "d-flex align-items-center gap-2"
            if kind = ColorToken then
                span { class' "border rounded flex-shrink-0"; attr "style" ("width:1.5rem;height:1.5rem;background:" + (if bad then "transparent" else value)); attr "aria-hidden" "true" }
            div {
                class' "flex-grow-1"
                textInput id value bad (if bad then id + "-help" else "") (fun v -> dispatch (EditToken(key, v)))
            }
        }
        if bad then div { id' (id + "-help"); class' "invalid-feedback d-block"; "Use a hex colour such as #0d6efd." }
    }

let private classRow (m: Model) (dispatch: Msg -> unit) (name: string, title: string) : Node =
    let id = "class-" + name
    div {
        class' "mb-2"
        label { attr "for" id; class' "form-label small mb-0"; title }
        textInput id (classes name m.Profile) false "" (fun v -> dispatch (EditClass(name, v)))
    }

let private themePanel (m: Model) (dispatch: Msg -> unit) : Node =
    div {
        h3 "Framework profile"
        div {
            class' "mb-3"
            label { attr "for" "profile-pick"; class' "form-label small fw-semibold mb-1"; "Profile" }
            select {
                id' "profile-pick"
                class' "form-select form-select-sm"
                value' m.Profile.Name
                attr "aria-describedby" "profile-help"
                onChange (fun v -> dispatch (PickProfile v))
                for p in builtIn do
                    option { value' p.Name; p.Name }
                if (byName m.Profile.Name).IsNone then option { value' m.Profile.Name; m.Profile.Name + " (imported)" }
            }
            div { id' "profile-help"; class' "form-text"; "The email is painted with the tokens (email clients ignore stylesheets); the web version uses the framework's class names and stylesheet. Everything below can be edited, exported and imported." }
        }
        details {
            attr "open" ""
            class' "mb-3"
            summary' { class' "fw-semibold small"; "Stylesheet and script" }
            div {
                class' "mt-2"
                label { attr "for" "profile-css"; class' "form-label small mb-0"; "Stylesheet address" }
                textInput "profile-css" m.Profile.Stylesheet false "" (fun v -> dispatch (EditStylesheet v))
                label { attr "for" "profile-js"; class' "form-label small mb-0 mt-2"; "Script address (optional)" }
                textInput "profile-js" m.Profile.Script false "" (fun v -> dispatch (EditScript v))
            }
        }
        details {
            attr "open" ""
            class' "mb-3"
            summary' { class' "fw-semibold small"; "Design tokens (email)" }
            div {
                class' "mt-2"
                for t in tokenKeys do tokenRow m dispatch t
            }
        }
        details {
            class' "mb-3"
            summary' { class' "fw-semibold small"; "Class names (web version)" }
            div {
                class' "mt-2"
                for r in classRoles do classRow m dispatch r
            }
        }
    }

let private panelNames : (Panel * string) list = [ PBlock, "Block"; PDocument, "Newsletter"; PTheme, "Theme" ]

let private right (m: Model) (dispatch: Msg -> unit) : Node =
    aside {
        ariaLabel "Settings"
        div {
            class' "mb-3"
            group "Settings panel" [ for (panel, name) in panelNames -> toggle name (m.Panel = panel) (fun () -> dispatch (SetPanel panel)) ]
        }
        match m.Panel with
        | PBlock -> blockPanel m dispatch
        | PDocument -> documentPanel m dispatch
        | PTheme -> themePanel m dispatch
    }

// ── centre: preview and code ──
let private previewFrame (m: Model) : Node =
    div {
        class' "preview-wrap rounded border bg-body-secondary p-2 d-flex justify-content-center"
        el "iframe" {
            class' (if m.Mobile then "preview-frame preview-mobile bg-white border-0 shadow-sm" else "preview-frame bg-white border-0 shadow-sm")
            attr "title" (if m.Medium = Email then "Preview of the email" else "Preview of the web version")
            attr "sandbox" "allow-scripts"
            attr "srcdoc" (previewHtml m)
        }
    }

let private outputNames : (Output * string * string * string) list =
    [ OutEmail, "Email HTML", "newsletter-email.html", "text/html"
      OutWeb, "Web HTML", "newsletter-web.html", "text/html"
      OutText, "Plain text", "newsletter.txt", "text/plain"
      OutJson, "Project (JSON)", "newsletter.json", "application/json" ]

let private codeView (m: Model) (dispatch: Msg -> unit) : Node =
    let name, file, mime = outputNames |> List.find (fun (o, _, _, _) -> o = m.Output) |> fun (_, n, f, t) -> n, f, t
    div {
        div {
            class' "d-flex flex-wrap gap-2 align-items-center mb-2"
            group "What to show" [ for (o, title, _, _) in outputNames -> toggle title (m.Output = o) (fun () -> dispatch (SetOutput o)) ]
            button { type' "button"; class' "btn btn-sm btn-outline-primary"; onClick (fun () -> dispatch Copy); "Copy" }
            button { type' "button"; class' "btn btn-sm btn-outline-primary"; data' "download" file; data' "source" "#export-text"; data' "type" mime; "Download" }
        }
        label { attr "for" "export-text"; class' "visually-hidden"; name }
        textarea {
            id' "export-text"
            class' "form-control font-monospace small export-text"
            attr "readonly" ""
            attr "spellcheck" "false"
            value' (output m)
        }
    }

let private severityBadge (f: Lint.Finding) : Node =
    let isError = (f.Severity = Lint.Error)
    let cls = if isError then "badge text-bg-danger" else "badge text-bg-warning"
    let text = if isError then "Error" else "Warning"
    span { class' cls; text }

let private checks (m: Model) (dispatch: Msg -> unit) : Node =
    let fs = findings m
    let errs = Lint.errors fs
    let warns = fs.Length - errs
    details {
        class' "mt-3"
        summary' {
            class' "fw-semibold small"
            "Checks: " + string errs + (if errs = 1 then " error, " else " errors, ") + string warns + (if warns = 1 then " warning" else " warnings")
        }
        if fs.IsEmpty then p "Nothing to fix: subject, alternative text, links, contrast and footer are in order."
        ul {
            class' "list-unstyled small mt-2"
            for f in fs do
                li {
                    class' "d-flex align-items-start gap-2 mb-1"
                    severityBadge f
                    span { class' "flex-grow-1"; f.Message }
                    match f.Block with
                    | Some id -> button { type' "button"; class' "btn btn-link btn-sm p-0"; onClick (fun () -> dispatch (Select(Some id))); "Show" }
                    | None -> Empty
                }
        }
    }

let private centre (m: Model) (dispatch: Msg -> unit) : Node =
    main' {
        id' "main"
        div {
            class' "d-flex flex-wrap gap-2 align-items-center mb-2"
            group "Surface" [ toggle "Preview" (m.Surface = Preview) (fun () -> dispatch (SetSurface Preview)); toggle "Code" (m.Surface = Code) (fun () -> dispatch (SetSurface Code)) ]
            if m.Surface = Preview then
                group "Version" [ toggle "Email" (m.Medium = Email) (fun () -> dispatch (SetMedium Email)); toggle "Web" (m.Medium = Web) (fun () -> dispatch (SetMedium Web)) ]
            if m.Surface = Preview then
                group "Width" [ toggle "Desktop" (not m.Mobile) (fun () -> dispatch (SetMobile false)); toggle "Phone" m.Mobile (fun () -> dispatch (SetMobile true)) ]
        }
        match m.Surface with
        | Preview -> previewFrame m
        | Code -> codeView m dispatch
        checks m dispatch
    }

// ── the page ──
let private topBar (m: Model) (dispatch: Msg -> unit) : Node =
    header {
        class' "navbar bg-body-tertiary border-bottom px-3 gap-2"
        span { class' "navbar-brand mb-0 h1 fs-5"; "Newsletter Designer" }
        span { class' "text-body-secondary small me-auto"; m.Doc.Name }
        div {
            class' "d-flex gap-2"
            button { type' "button"; class' "btn btn-sm btn-outline-dark"; disabled (not (canUndo m)); onClick (fun () -> dispatch Undo); "Undo" }
            button { type' "button"; class' "btn btn-sm btn-outline-dark"; disabled (not (canRedo m)); onClick (fun () -> dispatch Redo); "Redo" }
        }
    }

/// While a block is dragged: the line where it would drop, and its name under the pointer.  Neither takes the pointer (`pointer-events: none`), so the drop is
/// decided by what is under the pointer, not by the overlay.
let private dragOverlay (m: Model) : Node =
    let px (v: float) : string = string (int v) + "px"
    div {
        match dropMark m with
        | Some (x, y, w, h) ->
            div {
                class' "bg-primary rounded"
                data' "dnd" "mark"
                attr "style" ("position:fixed;pointer-events:none;z-index:1080;left:" + px x + ";top:" + px y + ";width:" + px w + ";height:" + px (max h 3.0))
                attr "aria-hidden" "true"
            }
        | None -> ()
        match dragGhost m with
        | Some (label, x, y) ->
            div {
                class' "bg-primary text-white small rounded shadow px-2 py-1"
                data' "dnd" "ghost"
                attr "style" ("position:fixed;pointer-events:none;z-index:1090;left:" + px (x + 12.0) + ";top:" + px (y + 12.0))
                attr "aria-hidden" "true"
                label
            }
        | None -> ()
    }

let view (m: Model) (dispatch: Msg -> unit) : Node =
    div {
        a { class' "visually-hidden-focusable"; href "#main"; "Skip to the preview" }
        topBar m dispatch
        div {
            class' "container-fluid py-3"
            div {
                class' "row g-3"
                div { class' "col-12 col-lg-3 order-2 order-lg-1"; left m dispatch }
                div { class' "col-12 col-lg-6 order-1 order-lg-2"; centre m dispatch }
                div { class' "col-12 col-lg-3 order-3"; right m dispatch }
            }
        }
        dragOverlay m
        div { role "status"; attr "aria-live" "polite"; class' "visually-hidden"; m.Notice }
        footer {
            class' "border-top px-3 py-2 small text-body-secondary"
            m.Notice
        }
    }
