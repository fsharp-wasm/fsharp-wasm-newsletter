# Projects: a library of newsletters in the browser

Until 2026-11-22 the designer kept exactly one project, under one key of the browser's local storage; "New empty issue" replaced it. Now it keeps a library. Nothing leaves the browser.

## What a person can do

- **Projects (n)** in the top bar opens the list: every newsletter with its name, subject line, number of blocks and when it was last saved (UTC), newest first. Clicking one opens it; the one that is open is marked.
- **New newsletter** makes an empty one, named "Untitled issue", "Untitled issue 2", … so that no two are alike, and opens it; it keeps the theme (profile) of the one before.
- **Duplicate** makes a copy ("name (copy)") beside the original; the open one stays open. **Delete** asks first; deleting the open one opens the newest of the rest, deleting the last one leaves a new empty newsletter.
- **Search** filters the list by a part of the name or the subject, whatever the case.
- **Import as a new newsletter** (document panel) takes a project file as a project of its own; **Import** still replaces the open one (and is one undo step).
- Every change is kept as it is made, under the id of the open project. The library opens on the project that was open when the page was left.
- The single project that earlier versions kept under one key becomes the first project of the library, and its old key is removed.

Undo is per open project: a project that is opened starts with a fresh log of changes.

## How it is built

- `Newsletter.Projects` (pure): a `Meta` per project (id, name, subject, blocks, created, modified), the library (the metas and the id that is open), ids (`p<n>`: one above the largest in use, no clock), names (numbered, copies), the search,
  the index as JSON (read leniently: an entry that is no project is skipped) and `stamp` (a date from milliseconds, by calendar arithmetic).
- `Newsletter.Studio` (pure): the state machine has the library, the messages (`NewProject`, `OpenProject`, `DuplicateProject`, `DeleteProject`, `FilterProjects`, `ImportAsProject`, …) and says what the host must do as effects:
  `Save(id, json)`, `SaveIndex`, `Load(id, intent)`, `Erase key`. It has no clock and no storage: the host answers `Saved(id, now)` after a save (`Browser.Timing.now`) and `Loaded(id, intent, json)` after a read, and gives `Boot`
  what the storage holds.
- Storage layout: `fsharp-wasm-newsletter/projects` is the index (small; a list needs one read), `fsharp-wasm-newsletter/project/<id>` is a project (the JSON the export produces).
- A project is never kept under another project's id: when the open one is deleted, or another is being read, nothing is open (`Current = ""`) until the answer is there, and a change in between is kept under no id (the host drops it).
  The tests check that.

## Not built

- No sync between browsers or devices, no shared projects, no versions beyond the undo log of the open one; the JSON export is the way to move or back up a project.
- The list shows when a project was saved, not when it was created; renaming is the "Name (only for you)" field of the open project.
- No folders or tags; the search is on name and subject only.
