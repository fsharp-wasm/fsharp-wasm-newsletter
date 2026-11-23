/// The designer as a browser module: hosts the program of `Newsletter.Studio` in the page, keeps the library of projects in the browser's
/// local storage (an index, and each project under a key of its own: `Newsletter.Projects`) and does the copying.  The page (web/index.html) calls `start`.
module Newsletter.Designer.NewsletterDesigner

open Browser.Elmish.Core
open Newsletter
open Newsletter.Studio

/// What the browser keeps under a key; "" when nothing.
let private read (key: string) : string =
    let value = Browser.Storage.Local.get key
    if isNull value then "" else value

/// The preview is an iframe in a sandbox that cannot touch this page: it and the designer talk through messages.
let private previewFrame = "iframe.preview-frame"

let private run' (effect: Effect) : Cmd<Msg> =
    match effect with
    // a project that is kept is stamped with the time (a pure `update` has no clock); no id: nothing is open, so nothing is kept
    | Save(id, json) ->
        if id = "" then []
        else [ fun dispatch -> Browser.Storage.Local.set (Projects.projectKey id) json; dispatch (Saved(id, Browser.Timing.now ())) ]
    | SaveIndex json -> [ fun _ -> Browser.Storage.Local.set Projects.indexKey json ]
    | Load(id, intent) ->
        [ fun dispatch ->
            let json = read (Projects.projectKey id)
            dispatch (Loaded(id, intent, (if json = "" then None else Some json))) ]
    | Erase key -> [ fun _ -> Browser.Storage.Local.remove key ]
    | Clipboard text -> [ fun dispatch -> Browser.Clipboard.writeText text (fun () -> dispatch (Copied true)) (fun _ -> dispatch (Copied false)) ]
    | ToPreview json -> [ fun _ -> Browser.Events.postToFrame previewFrame json ]
    | ScrollBy dy -> [ fun _ -> Browser.Dom.scrollBy 0.0 dy ]
    // the places are measured when the frame comes, after the scroll that was asked for before it
    | NextFrame -> [ fun dispatch -> Browser.Timing.requestAnimationFrame (fun () -> dispatch (Frame(Browser.Dom.viewportHeight (), Browser.Events.measureSlots (), Browser.Timing.now ()))) |> ignore ]

let private init () : Model * Cmd<Msg> =
    let hear : Cmd<Msg> = [ fun dispatch -> Browser.Events.onFrameMessage previewFrame (fun json -> dispatch (FromPreview json)) ]
    // the index, the project it was left on (when it names one), the single project of the versions before the library, and the time: Studio decides what they make
    let index = read Projects.indexKey
    let current = (match Projects.libraryOfJson index with Some library -> read (Projects.projectKey library.Current) | None -> "")
    let model, effects = Studio.update (Boot(index, current, read Projects.legacyKey, Browser.Timing.now ())) Studio.init
    model, hear @ (effects |> List.collect run')

let private update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    let next, effects = Studio.update msg model
    next, effects |> List.collect run'

let start () : unit =
    run "app" { Init = init; Update = update; View = View.view }
