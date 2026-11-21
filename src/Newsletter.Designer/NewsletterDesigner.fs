/// The designer as a browser module: hosts the program of `Newsletter.Studio` in the page, keeps the project in the browser's
/// local storage and does the copying.  The page (web/index.html) calls `start`.
module Newsletter.Designer.NewsletterDesigner

open Browser.Elmish.Core
open Newsletter
open Newsletter.Studio

let private storageKey = "fsharp-wasm-newsletter/project"

/// The preview is an iframe in a sandbox that cannot touch this page: it and the designer talk through messages.
let private previewFrame = "iframe.preview-frame"

let private run' (effect: Effect) : Cmd<Msg> =
    match effect with
    | Save json -> [ fun _ -> Browser.Storage.Local.set storageKey json ]
    | Clipboard text -> [ fun dispatch -> Browser.Clipboard.writeText text (fun () -> dispatch (Copied true)) (fun _ -> dispatch (Copied false)) ]
    | ToPreview json -> [ fun _ -> Browser.Events.postToFrame previewFrame json ]

let private init () : Model * Cmd<Msg> =
    let saved = Browser.Storage.Local.get storageKey
    let hear : Cmd<Msg> = [ fun dispatch -> Browser.Events.onFrameMessage previewFrame (fun json -> dispatch (FromPreview json)) ]
    if isNull saved || saved = "" then Studio.init, hear
    else fst (Studio.update (Restore saved) Studio.init), hear

let private update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    let next, effects = Studio.update msg model
    next, effects |> List.collect run'

let start () : unit =
    run "app" { Init = init; Update = update; View = View.view }
