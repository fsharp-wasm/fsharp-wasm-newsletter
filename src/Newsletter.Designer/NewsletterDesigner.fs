/// The designer as a browser module: hosts the program of `Newsletter.Studio` in the page, keeps the project in the browser's
/// local storage and does the copying.  The page (web/index.html) calls `start`.
module Newsletter.Designer.NewsletterDesigner

open Browser.Elmish.Core
open Newsletter
open Newsletter.Studio

let private storageKey = "fsharp-wasm-newsletter/project"

let private run' (effect: Effect) : Cmd<Msg> =
    match effect with
    | Save json -> [ fun _ -> Browser.Storage.Local.set storageKey json ]
    | Clipboard text -> [ fun dispatch -> Browser.Clipboard.writeText text (fun () -> dispatch (Copied true)) (fun _ -> dispatch (Copied false)) ]

let private init () : Model * Cmd<Msg> =
    let saved = Browser.Storage.Local.get storageKey
    if isNull saved || saved = "" then Studio.init, Cmd.none
    else fst (Studio.update (Restore saved) Studio.init), Cmd.none

let private update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    let next, effects = Studio.update msg model
    next, effects |> List.collect run'

let start () : unit =
    run "app" { Init = init; Update = update; View = View.view }
