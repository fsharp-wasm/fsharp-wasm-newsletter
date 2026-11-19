/// A starting point: a small issue that uses every kind of block.
module Newsletter.Sample

open Newsletter.Model

let issue : Document =
    let steps : (Kind * (Block -> Block)) list =
        [ Image, (fun b -> { b with Url = "https://placehold.co/600x240/0d6efd/ffffff/png?text=The+Weekly"; Alt = "The Weekly — a newsletter about small, fast software" })
          Heading, (fun b -> { b with Level = 1; Text = "Small software, big difference" })
          Paragraph, (fun b -> { b with Text = "Hello,\n\nthis issue is about programs that start quickly and stay out of the way. Three short stories, one idea each — **read what interests you**, skip the rest." })
          Button, (fun b -> { b with Text = "Read the main story"; Url = "https://example.com/story" })
          Divider, id
          Heading, (fun b -> { b with Level = 2; Text = "Also this week" })
          Columns, (fun b -> { b with Text = "**Faster starts.** A module that is a few kilobytes loads before the page has finished painting. [How we measured](https://example.com/measure)"; Text2 = "**Fewer dependencies.** Every package you add is code you answer for. _Choose them like colleagues._" })
          Quote, (fun b -> { b with Text = "Simplicity is a prerequisite for reliability."; Text2 = "Edsger W. Dijkstra" })
          Spacer, id
          Footer, (fun b -> { b with Text = "You are receiving this because you subscribed to The Weekly."; Text2 = "Example Ltd · 1 Main Street · 12345 Town" }) ]
    let start = { emptyDocument with Name = "The Weekly #1"; Subject = "Small software, big difference"; Preheader = "Three short stories about programs that stay out of the way." }
    steps |> List.fold (fun d (kind, tweak) -> let d2, id = addBlock kind None d in updateBlock id tweak d2) start
