/// Moving a block by dropping it: the blocks after the drop, as the shared library computes them (FSharp.Wasm.Edit knows that a move reads its target without
/// the block, which shifts the indices behind it, and that dropping a block where it already is changes nothing).
module Newsletter.Dnd

open Newsletter.Model
open FSharp.Wasm.Edit

/// The blocks are a flat list: no block has children.
let blockShape : Shape<Block> = { Kids = (fun _ -> []); WithKids = (fun b _ -> b) }

/// The blocks after dropping block `id` before block `target`, or, with `after`, behind it.  None when that changes nothing (onto itself, or right where it already is).
let dropBlock (id: int) (target: int) (after: bool) (blocks: Block list) : Block list option =
    match List.tryFindIndex (fun (b: Block) -> b.Id = id) blocks, List.tryFindIndex (fun (b: Block) -> b.Id = target) blocks with
    | Some source, Some t ->
        (match Ops.drop blockShape [ source ] (if after then After [ t ] else Before [ t ]) blocks with
         | Some op ->
            (match Ops.apply blockShape op blocks with
             | Ok moved -> Some moved
             | Error _ -> None)
         | None -> None)
    | _ -> None
