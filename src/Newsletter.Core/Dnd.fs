/// Moving a block by dropping it, and putting a new one where it is dropped: the blocks after the drop, as the shared library computes them (FSharp.Wasm.Edit
/// knows that a move reads its target without the block, which shifts the indices behind it, and that dropping a block where it already is changes nothing).
module Newsletter.Dnd

open Newsletter.Model
open FSharp.Wasm.Edit

/// The blocks are a flat list: no block has children.
let blockShape : Shape<Block> = { Kids = (fun _ -> []); WithKids = (fun b _ -> b) }

/// The slot of the empty list ("No blocks yet"): a new block dropped there is the first.  No block has this number.
let endOfList = 0

let private run (op: Op<Block> option) (blocks: Block list) : Block list option =
    op |> Option.bind (fun o -> match Ops.apply blockShape o blocks with Ok changed -> Some changed | Error _ -> None)

let private place (blocks: Block list) (target: int) (after: bool) : Place option =
    blocks |> List.tryFindIndex (fun b -> b.Id = target) |> Option.map (fun t -> if after then After [ t ] else Before [ t ])

/// The blocks after dropping block `id` before block `target`, or, with `after`, behind it.  None when that changes nothing (onto itself, or right where it already is).
let dropBlock (id: int) (target: int) (after: bool) (blocks: Block list) : Block list option =
    match List.tryFindIndex (fun (b: Block) -> b.Id = id) blocks, place blocks target after with
    | Some source, Some at -> run (Ops.drop blockShape [ source ] at blocks) blocks
    | _ -> None

/// The blocks after putting the new block `b` before block `target`, or, with `after`, behind it; on the slot of the empty list it is the only one.
let insertBlock (b: Block) (target: int) (after: bool) (blocks: Block list) : Block list option =
    if target = endOfList then run (Some (Insert ([ blocks.Length ], b))) blocks
    else place blocks target after |> Option.bind (fun at -> run (Ops.insertAt blockShape at b blocks) blocks)
