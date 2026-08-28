# Roadmap

Where the kit is headed, ordered by impact. The two goals: make **prototyping the narrative** effortless, and make **importing the narrative into a real game** something a team can ship with. Done so far: entity assets + drag-and-drop (0.5.0), story simulator, playtest rewind panel, location anchors, generated id constants, welcome/guide window (0.6.0).

Each item lists why it matters, what to build, and rough size (S ≈ hours, M ≈ a day-ish, L ≈ several days).

## 1. Presentation interfaces (M) — highest leverage for shipping
**Why:** the biggest blocker to using the kit in a real game is that `DialogueUI`, `InventoryHUD`, `LocationBanner` and `PickupToast` are concrete uGUI classes the director holds directly. Studios have their own UI stack (TMP, UI Toolkit, speech bubbles, comic panels).
**What:** extract `IDialogueUI` / `IInventoryUI` / `IBannerUI` / `IToastUI` interfaces mirroring the current public methods (`Say`, `Narrate`, `Choose`, `ShowBark`, `Refresh`, `Show`…); director fields become interface references resolved via serialized `MonoBehaviour` + cast (Unity can't serialize interfaces) or a small locator; the kit's uGUI widgets become the default implementations. Ship one alternate implementation (world-space speech bubble) as proof.
**Depends on:** nothing. **Unlocks:** #7 (VO), custom sample (#12).

## 2. Runner + simulator test suite (M) — the foundation everything leans on
**Why:** the runner is pure C# and the simulator's honesty depends on it; several past bugs (gating, availability, effects) would have been caught by tests. Cheap insurance for every item below.
**What:** an `.asmdef`'d editor test assembly; fixture stories as JSON strings; tests for traversal (choice/check/random/jump), conditions and effects, item prefix handling, strict-order availability, save/restore round-trips, and simulator ground truths (a story with a known soft-lock must report it; one without must not).
**Depends on:** nothing. Do early — ideally alongside #1.

## 3. Localization export (M)
**Why:** retrofitting localization after text is referenced everywhere is 10× the cost of doing it while all text still flows through the runner and kit UI.
**What:** exporter writing every line/title/description/choice label to CSV (and Unity Localization String Tables when the package is present), keyed `nodeId/lineIndex/field`; a `Func<string key, string fallback, string>` hook on `StoryRunner`/director that all display paths call; re-import diff report (new/changed/removed keys).
**Depends on:** nothing; pairs well with #1.

## 4. Event registry asset (S)
**Why:** Event nodes currently reach game code through one `OnStoryEvent(string)` — every consumer writes a switch on strings.
**What:** generated `StoryloomEventRegistry` ScriptableObject with one row per event name (id via `StoryIds.Events`), each exposing a `UnityEvent`; a scene component that subscribes to the director and dispatches. Designers wire cutscenes/unlocks in the inspector; coders can also subscribe per-event.
**Depends on:** id codegen (done).

## 5. Live story re-import during play (S/M)
**Why:** the writer loop — edit in Storyloom, alt-tab, keep playing — currently means restarting play mode.
**What:** file watcher on the source `.unity.json` (or a "Reload story" button on the playtest panel); on change: `StoryloomStoryAsset.Invalidate()`, rebuild the runner's `Story` reference while keeping `Variables`, `Played`, `PendingNodeId` (all id-keyed, so they survive); log ids that vanished. The playtest panel's rewind history covers the "state is now weird" escape hatch.
**Depends on:** playtest panel (done).

## 6. Playthrough transcripts (S)
**Why:** the artifact writers actually review. Nearly free off the existing events.
**What:** a recorder (toggle on the playtest panel + a director flag) capturing beats, lines, choices made, variable changes, timestamps; export as screenplay-style text/markdown to a file; "copy to clipboard" button. Simulator could emit the same format for a soft-lock's reproduction path.
**Depends on:** nothing.

## 7. Voice-over pipeline (M)
**Why:** lines are already id-stable and `Character.voiceSamples` exists in the data — the plumbing is half there.
**What:** generated per-line audio table (id-keyed ScriptableObject); auto-match clips by filename convention (`<nodeId>_<lineIndex>.*`) with a report of missing/orphaned files; `DialogueUI.Say` (or the `IDialogueUI` implementations) plays the line clip, typewriter duration syncs to clip length when present.
**Depends on:** #1 helps, not required.

## 8. Prefab variant batch-bind (S)
**Why:** the per-entity drag exists; teams with one base NPC controller want the batch version.
**What:** window action "Generate character prefabs": pick a base prefab, get one prefab *variant* per character with the entity asset applied (`ApplyTo` on the variant root) into a folder, bindings updated to point at them. Same for items/discoverables.
**Depends on:** entity assets (done).

## 9. Visual story graph window (L)
**Why:** the F1 HUD tells you *what* the kit sees; a graph tells you *where you are in the whole story* — and doubles as the simulator's result display (soft-lock paths highlighted on the graph).
**What:** GraphView-based window laying out nodes (export order → columns, links → edges); play-mode overlay coloring played/current/pending/gated/locked; click a node → play it (via the playtest machinery); simulator overlay coloring never-played and soft-lock trails.
**Depends on:** playtest (done), simulator (done). Biggest single effort here — schedule accordingly.

## 10. Save-system hardening (M)
**Why:** `SaveJson`/`LoadJson` assume the story hasn't changed between save and load, and every game needs slots.
**What:** version + story-hash stamp in the blob; migration pass on load (drop unknown ids with a report, coerce changed variable types); `ISaveStore` abstraction (file-based default) with slot list/save/load/delete; hook for games to add their own payload alongside the story state.
**Depends on:** nothing; #2's round-trip tests first make this much safer.

## 11. One-click share build (S/M)
**Why:** prototypes are for showing people; "send a link" beats "install Unity".
**What:** window action that switches to WebGL (with a confirm — platform switches are slow), builds the current test scene to a folder, zips it, and opens the output; template index.html with the story's name. Optional itch.io butler push if configured.
**Depends on:** nothing.

## 12. "Real integration" sample (M, after #1)
**Why:** LanternRoad shows the minimal player; nothing shows the production path end-to-end.
**What:** a second sample under `Samples~`: a small hand-built level using location anchors, a custom `IDialogueUI` (speech bubbles), `StoryIds` in a quest script, and the event registry. The sample *is* the documentation for items 1, 4, 8.
**Depends on:** #1, #4, anchors (done).

## Sequencing suggestion
1–2 together (interfaces + tests), then 3–4–5 as a cluster (all small, all compounding), 6–7–8 opportunistically, 9 when there's appetite for a big one, 10–11–12 on the road to a first external team using the kit.
