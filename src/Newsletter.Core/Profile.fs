/// A framework profile: the design tokens (what an email is painted with: email clients ignore stylesheets, so the tokens
/// become inline styles), the class map (what the web version is marked up with: real class names of the framework) and the
/// stylesheet that makes those classes mean something.  Bootstrap 5.3 is the default; every part can be edited.
module Newsletter.Profile

open Newsletter.Json

type TokenKind = ColorToken | TextToken | NumberToken

/// key, label, kind — the order of the editor.
let tokenKeys : (string * string * TokenKind) list =
    [ "primary", "Primary", ColorToken; "onPrimary", "Text on primary", ColorToken
      "secondary", "Secondary", ColorToken; "onSecondary", "Text on secondary", ColorToken
      "text", "Text", ColorToken; "muted", "Muted text", ColorToken
      "canvas", "Background", ColorToken; "surface", "Content background", ColorToken; "border", "Borders", ColorToken
      "font", "Font stack", TextToken; "size", "Base size (px)", NumberToken
      "radius", "Corner radius (px)", NumberToken; "width", "Content width (px)", NumberToken ]

/// role, label — what the web version asks the framework for.
let classRoles : (string * string) list =
    [ "container", "Page container"; "h1", "Heading 1"; "h2", "Heading 2"; "h3", "Heading 3"; "p", "Paragraph"
      "button-primary", "Button: primary"; "button-secondary", "Button: secondary"; "button-outline", "Button: outline"
      "quote", "Quote"; "divider", "Divider"; "image", "Image"; "footer", "Footer"; "row", "Columns: row"; "col", "Columns: column"
      "left", "Align left"; "center", "Align centre"; "right", "Align right" ]

type Profile =
    { Name: string
      /// framework stylesheet (a CDN address or your own)
      Stylesheet: string
      /// optional script some frameworks need (Tailwind's browser build)
      Script: string
      Tokens: (string * string) list
      Classes: (string * string) list }

let bootstrapUrl = "https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css"

let private stack = "-apple-system, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif"

let bootstrap : Profile =
    { Name = "Bootstrap 5.3"
      Stylesheet = bootstrapUrl
      Script = ""
      Tokens =
        [ "primary", "#0d6efd"; "onPrimary", "#ffffff"; "secondary", "#6c757d"; "onSecondary", "#ffffff"
          "text", "#212529"; "muted", "#6c757d"; "canvas", "#f8f9fa"; "surface", "#ffffff"; "border", "#dee2e6"
          "font", stack; "size", "16"; "radius", "6"; "width", "600" ]
      Classes =
        [ "container", "container py-4"; "h1", "display-6 fw-bold mb-3"; "h2", "h3 fw-semibold mb-3"; "h3", "h5 fw-semibold mb-2"
          "p", "mb-3"; "button-primary", "btn btn-primary"; "button-secondary", "btn btn-secondary"
          "button-outline", "btn btn-outline-primary"; "quote", "blockquote border-start border-4 ps-3 my-4"
          "divider", "my-4"; "image", "img-fluid"; "footer", "text-body-secondary small mt-4"
          "row", "row g-4"; "col", "col-md-6"; "left", "text-start"; "center", "text-center"; "right", "text-end" ] }

let tailwindUrl = "https://cdn.tailwindcss.com/3.4.16"

let tailwind : Profile =
    { Name = "Tailwind CSS 3"
      Stylesheet = ""
      Script = tailwindUrl
      Tokens =
        [ "primary", "#2563eb"; "onPrimary", "#ffffff"; "secondary", "#475569"; "onSecondary", "#ffffff"
          "text", "#0f172a"; "muted", "#475569"; "canvas", "#f1f5f9"; "surface", "#ffffff"; "border", "#e2e8f0"
          "font", "ui-sans-serif, system-ui, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif"; "size", "16"; "radius", "8"; "width", "600" ]
      Classes =
        [ "container", "mx-auto max-w-2xl px-4 py-8"; "h1", "text-3xl font-bold tracking-tight mb-4"; "h2", "text-2xl font-semibold mb-3"
          "h3", "text-xl font-semibold mb-2"; "p", "mb-4 leading-relaxed"
          "button-primary", "inline-block rounded-lg bg-blue-600 px-5 py-3 font-semibold text-white no-underline hover:bg-blue-700"
          "button-secondary", "inline-block rounded-lg bg-slate-600 px-5 py-3 font-semibold text-white no-underline hover:bg-slate-700"
          "button-outline", "inline-block rounded-lg border border-blue-600 px-5 py-3 font-semibold text-blue-700 no-underline hover:bg-blue-50"
          "quote", "border-l-4 border-blue-600 pl-4 my-6 italic"; "divider", "my-6 border-slate-200"; "image", "max-w-full h-auto"
          "footer", "mt-8 text-sm text-slate-600"; "row", "grid gap-6 md:grid-cols-2"; "col", ""
          "left", "text-left"; "center", "text-center"; "right", "text-right" ] }

/// Starts as a copy of Bootstrap with nothing to load, so that every part is yours to fill in.
let custom : Profile =
    { bootstrap with Name = "Custom"; Stylesheet = ""; Classes = classRoles |> List.map (fun (k, _) -> k, "") }

let builtIn : Profile list = [ bootstrap; tailwind; custom ]
let defaultProfile : Profile = bootstrap

let tokenOr (fallback: string) (key: string) (p: Profile) : string =
    match p.Tokens |> List.tryFind (fun (k, _) -> k = key) with
    | Some(_, v) when v.Trim() <> "" -> v.Trim()
    | _ -> fallback

/// A token's value, the Bootstrap value when the profile has none.
let token (key: string) (p: Profile) : string =
    tokenOr (bootstrap.Tokens |> List.find (fun (k, _) -> k = key) |> snd) key p

let tokenInt (key: string) (p: Profile) : int =
    match Newsletter.Model.parseInt (token key p) with
    | Some n -> n
    | None -> Newsletter.Model.parseInt (bootstrap.Tokens |> List.find (fun (k, _) -> k = key) |> snd) |> Option.defaultValue 0

/// Classes for a role ("" when the profile has none).
let classes (role: string) (p: Profile) : string =
    match p.Classes |> List.tryFind (fun (k, _) -> k = role) with Some(_, v) -> v.Trim() | None -> ""

let setToken (key: string) (value: string) (p: Profile) : Profile =
    let has = p.Tokens |> List.exists (fun (k, _) -> k = key)
    { p with Tokens = if has then p.Tokens |> List.map (fun (k, v) -> if k = key then k, value else k, v) else p.Tokens @ [ key, value ] }

let setClasses (role: string) (value: string) (p: Profile) : Profile =
    let has = p.Classes |> List.exists (fun (k, _) -> k = role)
    { p with Classes = if has then p.Classes |> List.map (fun (k, v) -> if k = role then k, value else k, v) else p.Classes @ [ role, value ] }

let byName (name: string) : Profile option = builtIn |> List.tryFind (fun p -> p.Name = name)

// ── JSON: a profile is a file people can share ──
let toJson (p: Profile) : Json =
    let pairs (xs: (string * string) list) = JObj(xs |> List.map (fun (k, v) -> k, JStr v))
    JObj [ "name", JStr p.Name; "stylesheet", JStr p.Stylesheet; "script", JStr p.Script; "tokens", pairs p.Tokens; "classes", pairs p.Classes ]

let ofJson (j: Json) : Profile =
    let pairs (name: string) (fallback: (string * string) list) =
        match field name j with
        | Some(JObj fields) -> fields |> List.choose (fun (k, v) -> match v with JStr s -> Some(k, s) | JNum n -> Some(k, string n) | _ -> None)
        | _ -> fallback
    { Name = str "name" "Custom" j
      Stylesheet = str "stylesheet" "" j
      Script = str "script" "" j
      Tokens = pairs "tokens" []
      Classes = pairs "classes" [] }
