/// Icons in the view: the shapes of FSharp.Wasm.Icons as elements of the DOM view (an `svg` with an element per shape), so that a render keeps them and updates what
/// changed.  Decorative: the control around an icon carries the name.
module Newsletter.Designer.Icon

open Browser.Html.Elements
open FSharp.Wasm.Icons
open Newsletter.Model

let private attributes (pairs: (string * string) list) : Attr list = pairs |> List.map Attribute

/// An icon at `size` pixels, in the colour of the text around it.
let node (size: int) (icon: Icon) : Node =
    El("svg",
       Class "icon" :: attributes (Icons.svgAttributes size),
       icon.Shapes |> List.map (fun shape -> let tag, attrs = Icons.element shape in El(tag, attributes attrs, [])))

/// The icon of a kind of block.
let ofKind (kind: Kind) : Icon =
    match kind with
    | Heading -> Lucide.heading
    | Paragraph -> Lucide.text
    | Image -> Lucide.image
    | Button -> Lucide.mousePointerClick
    | Quote -> Lucide.quote
    | Divider -> Lucide.minus
    | Spacer -> Lucide.moveVertical
    | Columns -> Lucide.columns2
    | Footer -> Lucide.panelBottom
