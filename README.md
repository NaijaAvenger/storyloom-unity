# Storyloom → Unity

## Install

**From a git URL (recommended).** Unity ▸ Window ▸ Package Manager ▸ **+** ▸ *Add package from git URL…* and paste one of:

```
https://github.com/<you>/storyloom-unity.git                      # this package as its own repo
https://github.com/<you>/storyloom.git?path=/unity                # the same folder inside the Storyloom site repo
https://github.com/<you>/storyloom-unity.git#v0.2.0               # pin a tag
```

Or add it to `Packages/manifest.json`:

```json
"com.storyloom.unity": "https://github.com/<you>/storyloom-unity.git"
```

**From disk.** Package Manager ▸ **+** ▸ *Add package from disk…* ▸ pick `package.json`. Or copy the folder into `Packages/`.

Then **Window ▸ Storyloom** appears. The example story is under Package Manager ▸ Storyloom ▸ *Samples* ▸ Import.

Requires Unity 2021.3+, uGUI (`com.unity.ugui`) and the **Input System** package (`com.unity.inputsystem`), both declared as dependencies and installed automatically. In *Project Settings ▸ Player ▸ Active Input Handling* choose **Input System Package (New)** or **Both** (Unity prompts to restart). No TextMeshPro needed.

## Layout

```
package.json                 com.storyloom.unity
Runtime/                     Storyloom.Runtime.asmdef
  StoryloomData.cs           data classes (JsonUtility)
  StoryloomRunner.cs         graph runner — engine-agnostic
  Kit/                       director, player, interactables, UI, bindings, key binds
Editor/                      Storyloom.Editor.asmdef — Window ▸ Storyloom
Samples~/LanternRoad/        example export + minimal runtime-only player
```

Two layers:

1. **Runtime (data + runner)** — `StoryloomData.cs`, `StoryloomRunner.cs`. Loads the `.unity.json` and plays the graph: variables, conditions, effects, inventory, choices, checks, random, jumps, events, discoverables. Engine-agnostic C#; no prefabs, no UI.
2. **Kit (`Kit/`, `Editor/`)** — the companion tool. Imports the JSON into a **Story asset**, gives you a **Bindings asset** where every character, item, location and discoverable gets a prefab / sprite / scene, validates the whole thing, and can build a **playable Stardew-style scene** in one click: top-down player, NPCs you talk to, items you pick up, discoverables you examine, location banners, a dialogue box with portraits and choices, an inventory. Nothing is hidden — it generates ordinary GameObjects and uGUI you can restyle or replace.

## What the export contains

`File ▸ Export Unity JSON` in Storyloom writes `<story>.unity.json`, `format: storyloom-unity`, `version: 2`. It is flat and JsonUtility-friendly (public fields, arrays, no dictionaries, no nulls):

| Section | What's in it |
|---|---|
| `variables` | name, type (`bool int float string`), default, `tracked` |
| `characters` | id, name, description, image (data URI), roleType, role, speciesId, factionId, homeLocationId, traits, motivation, voice, relationships[], voiceSamples[] |
| `factions`, `species`, `regions`, `locations`, `items`, `lore` | typed world data; locations carry `regionId` (regions nest via `parentRegionId`), items carry `startOwned` |
| `nodes` | every flow node (notes / tech / design / narrative notes are **not** exported): id, type, title, text, textId, speakerId, locationId, when, why, image, lines[] (speakerId, text, emotion, stringId), characterIds / factionIds / itemIds / speciesIds / loreIds, conditions[], conditionMode, effects[], options[] (choice/random, with weight and conditions), links[] (port → toNodeId, label, conditions), jumpToNodeId, eventName, hostNodeId + discoverKind (discoverables), status, labels |
| `startNodeId` | where the runner starts |

Layout, comments, groups, version history and the four note types stay in the `.storyloom.json` project file — they're editor-only.

What the **runner** does with it: `StoryRunner.Start()` enters the start node and applies its effects; `GetOptions()` lists the ways forward (locked ones included with a human reason); `Choose()` moves; checks branch on pass/fail; `PickRandom()` rolls; jumps pass through; Event nodes raise `OnEvent(name)`; `GetDiscoverables()` lists optional content at the current node; `HasItem/GiveItem/TakeItem/Inventory()` handle the `item:` variables; `SnapshotState/RestoreState` save and load. String IDs (`textId`, `stringId`) match the CSV from Storyloom's strings export for localization.

What the export does **not** do on its own: it has no idea what a "character" looks like, where a location is in your world, or what "pick up" means. That is the kit's job.

## The kit in five steps

1. **Import.** Unity ▸ *Window ▸ Storyloom* ▸ **Import story JSON…** → pick the export. You get `Assets/Storyloom/Data/<story>.story.asset` (wraps the JSON) and `<story>.bindings.asset` (one row per character, item, location, discoverable). Re-importing a newer export refreshes names and adds new rows without touching your assignments.
2. **Bind.** In the window's tabs assign, per entity: **Characters** → prefab, portrait sprite, world sprite, voice bark; **Items** → prefab, icon; **Locations** → scene name (optional), trigger prefab, banner art, ambience; **Discoverables** → prefab, sprite. Leave a prefab empty and the *default* prefab for that kind is used.
3. **Placeholders.** **Create placeholder prefabs** makes coloured-square prefabs with the right component already on them (`NpcInteractable`, `ItemPickup`, `DiscoverableInteractable`), a floating name label and an `[E]` prompt — enough to play with before art exists.
4. **Validate.** The *Validate* tab lists anything unbound, dangling links, missing locations, unreachable nodes, engine events you'll need to handle, and "who says what where" per character.
5. **Create test scene.** Pick a **Game style** first — *Top-down (Stardew)*, *Third person* or *First person* — then build. Every style gets the same director, interactables and UI; only the player, camera and world plane differ (see *Game styles* below). Builds a scene: camera with smooth follow, player, `StoryloomDirector`, one cluster per location (floor, trigger volume, signpost) with the NPCs whose home is there (or who speak there), items given there, and discoverables hosted at beats set there; anything unplaced lands in a *Backstage* cluster; plus the full UI. Press Play.

**Live link.** Set the **Live link URL** on the story (shown in the window once a story is imported) to the endpoint that serves the workbook's Unity JSON — the same payload as *File ▸ Export Unity JSON* on storyloom.com — and **Re-sync from story** stops being local: it pulls the workbook's current export, overwrites the imported JSON in place (every reference keeps working), refreshes binding rows, regenerates entity assets and `StoryIds.cs` when they exist, and reports what changed ("+2 characters · −1 node"). An optional access token is sent as `Authorization: Bearer …` and stored per-machine in EditorPrefs, never in the project. Without a URL, Re-sync keeps its old local behaviour: re-read the already-imported JSON and add binding rows for new entities.

## Entity assets — drag-and-drop into your own project

The test scene is one use of the data; the other is wiring the story into a game you're already building. **Generate entity assets** (in the window's toolbar) creates one ScriptableObject per character, item, location and discoverable under `Assets/Storyloom/Entities/`. Each asset is a *typed handle* into the story — it carries the entity's id plus references to the story and bindings assets, and resolves everything live (`.Data`, `.DisplayName`, `.Portrait`, `.Prefab`…). Nothing is copied, so there is exactly one source of truth: re-import the story and every asset sees the new data; assets are matched by id, so renames upstream just rename the file and never break a scene reference.

Then wire things up by dragging:

- **Onto a GameObject** (Hierarchy or Scene view): the object gains the matching component, wired to that entity — `NpcInteractable` for a character, `ItemPickup` for an item, `DiscoverableInteractable` for a discoverable, `LocationTrigger` (plus a trigger volume, on the right physics plane for the current style) for a location. **Alt-drop** a location to get a `Signpost` instead. Your own components, colliders and visuals are left alone; an existing interactable of that kind is rebound rather than duplicated.
- **Onto empty ground in the Scene view**: the entity's bound prefab (or the default placeholder) is spawned at the drop point, wired.
- **Onto a prefab in the Project window**: the prefab itself gains the wired component — every instance of it, in every scene, becomes that entity. Other Project-window drags (moving files between folders) are untouched.
- **Into a component's asset field**: every interactable now has an asset slot (`character`, `item`, `discoverable`, `location`) next to its id string; drop an asset in and the id follows it, in the editor and again at runtime — so a prefab holding an `NpcInteractable` with a character asset assigned *is* that character, dialogue, gating and all.

The window's **Entities** tab is a palette of the same assets — drag rows straight from it instead of hunting through the Project window. Once assets exist, **Create test scene** and **Repair open scene** stamp them onto every interactable they place or find (matching by id, never overwriting an assigned one), so generated scenes reference the typed handles too.

From your own tooling, `asset.ApplyTo(gameObject)` does the same thing in code. The interactables keep working from bare id strings too — assets are a convenience layer, not a requirement.

Two more bridges into a real project:

- **Location anchors.** The generated test scene lays locations out in an artificial lane; a `LocationAnchor` goes the other way. Drop one into *your* level, assign the location's entity asset, and **Populate from story** (in its inspector) places that location's NPCs, items, discoverables and signpost under it — landing on your hand-placed `StoryloomSpawnPoint` children first (id-reserved ones matched first), then a grid around the anchor — plus the zone trigger. Populate is idempotent (spawns are marked and skipped next time) and **Clear generated** removes only what it created.
- **Id constants.** *Generate entity assets* also writes `Assets/Storyloom/StoryIds.cs`: `StoryIds.Characters.SisterElowen`, `StoryIds.Events.LanternLit`, etc. Game code that references the story stops being stringly-typed — typos and upstream renames become compile errors instead of silent no-ops.

## Testing the narrative

- **Simulator** (*Window ▸ Storyloom Simulator*, no scene or play mode needed): explores the story headlessly — every startable beat, every choice branch, every random outcome, from every reachable state (deduplicated by played-beats + variables, capped). Reports what static validation can't see: **soft-locks** (states where nothing can start and no ending was reached, with the shortest reproduction path), **endings no path reaches**, and **beats never played**. Model assumptions are documented in `StoryloomSimulator.cs`: the player can walk anywhere and talk to anyone (gates pause, they don't block), and played beats aren't replayed.
- **Playtest panel** (*Window ▸ Storyloom Playtest*, in play mode): jump to any beat (bypasses strict order), **rewind** to before any beat already played (the director snapshots full story state per beat — variables, inventory, played set, pending, location; world objects a pickup destroyed stay gone), edit variables and inventory live, and flip the gating toggles. Iterating on beat 40 no longer means earning beats 1–39.

A **Welcome / guide** window covers all of this in-editor; it opens on first import and can be set to show on every project start or never (*Window ▸ Storyloom Welcome* reopens it any time). The kit also checks once per editor start whether a newer version exists on the repo and prompts when one does — the prompt has a checkbox to silence itself, and *Window ▸ Storyloom Check for Updates* checks by hand.

## Custom UI — bring your own presentation

The director talks to its UI only through four interfaces (`StoryloomUIContracts.cs`): `IDialogueUI` (lines, narration, choices, barks — the coroutines finish when the player has advanced), `ILocationBannerUI`, `IPickupToastUI` and `IInventoryUI`. The kit's uGUI widgets are just the default implementations. To swap in your own stack — TextMeshPro, UI Toolkit, speech bubbles, comic panels — implement the matching interface on any MonoBehaviour and drag it into the director's **Custom UI** override slot; the built-in widget is then ignored entirely. `SpeechBubbleUI` (world-space bubbles over whoever is speaking) is a complete, deliberately small example meant to be copied.

The package ships assembly definitions (`Storyloom.Runtime` / `Storyloom.Editor` / `Storyloom.Tests`); scripts in `Assets/` keep compiling unchanged, and your own asmdefs can reference `Storyloom.Runtime` directly. An edit-mode test suite covers the runner and the simulator — add `"testables": ["com.storyloom.unity"]` to `Packages/manifest.json` to see it in the Test Runner.

## Game styles

The same story can be test-played three ways. The choice is stored on the Bindings asset (`gameStyle`) and shown as a toolbar in the window; **Create test scene** builds the matching scene and swaps the *default* placeholder prefabs to the matching kind (your own prefabs are never touched — 3D placeholders live in `Placeholders/3D/`).

| Style | World | Player | Camera | Interact |
|---|---|---|---|---|
| Top-down (Stardew) | XY plane, 2D physics | `PlayerController2D` (Rigidbody2D) | orthographic, `SimpleFollow` | nearest interactable in reach, preferring what you face |
| Third person | XZ plane (y up), 3D physics | `PlayerController3D` (CharacterController; WASD relative to the camera) | `ThirdPersonCamera` orbits behind — mouse / right stick | nearest in reach, preferring what you face |
| First person | XZ plane, 3D physics | `FirstPersonController` (mouse look, pitch on the head) | child of the head, crosshair | what you look at (a sphere sweep from the camera, `aimRadius`), else nearest in front |

**Reach** is measured from the player to the nearest point on the target's *collider*, not between the two pivots, and the range test happens while the winner is picked — so a wide NPC, an off-centre pivot or two candidates at similar distances can't leave you with "nothing to interact with here". `Reach` = the binds' `interactRadius` + 1.1. **Zones** (`LocationTrigger`) fire from physics trigger events *and* a containment poll that respects the volume's rotation and scale, with `exitSlack` of hysteresis so standing on a boundary doesn't flicker; the arrival popup shows again whenever the player re-enters a zone they had left. The F1 debug HUD prints which zones geometrically contain the player, which separates "the volume is in the wrong place" from "the trigger callback went missing".

All three share `StoryloomPlayer` (the base the interactables and director talk to), the `Interactable` family, `LocationTrigger` (2D or 3D trigger), the dialogue box, banner, toast, inventory, story map and debug HUD. In the 3D styles `CursorLock` keeps the mouse captured while free-roaming and releases it during beats, with the inventory open, or on **Esc** (click to grab it again); `mouseSensitivity`, `stickSensitivity` and `invertY` are on the key-binds asset. Labels and prompts on 3D placeholders billboard to the camera. *Repair open scene* and the runtime self-repair are style-aware (2D colliders for top-down, 3D colliders otherwise).

To retheme: replace the player object with your own controller that derives from `StoryloomPlayer` (implement `Style`, set `Focus`, call `HandleActionKeys()`), keep everything else.

## The Stardew-style binds

`StoryloomKeyBinds` asset (editable `Key` fields; InputActions are built from them at runtime — no .inputactions file): **WASD / arrows / left stick** move, **Shift / stick click** run, **E or Space / south button** interact (talk / pick up / examine) and advance dialogue, **↑↓ / d-pad + E** pick a choice, **Esc / east** cancel, **Tab / north** inventory, **hold M** story map (where you are, what's next, endings reached — reads *ALL ROUTES COMPLETED* once every reachable ending has been seen), **J** journal (reserved). Walk speed, run speed and interact radius live there too.

| Component | Put it on | What it does |
|---|---|---|
| `PlayerController2D` / `PlayerController3D` / `FirstPersonController` | the player | movement, facing, finds the nearest `Interactable` in reach (first person: what you look at), shows its prompt, freezes during beats; all derive from `StoryloomPlayer` |
| `NpcInteractable` | a character prefab | `characterId`; on E → `Director.TalkTo(id)`: the best **unplayed** beat this character speaks in **at this location** (conditions satisfied), else any unplayed, else a replay; optional `preferredNodeIds` list to script it |
| `ItemPickup` | an item prefab | `itemId`; on E → `GiveItem` + toast, then destroyed (and auto-removed on load if already owned) |
| `DiscoverableInteractable` | a world object | `nodeId` of a Discoverable; label shows title, kind and reward ("secret · gold +5"); prompt shows the lock reason; on E → plays the node (text, effects, its *Then* link or back to where it was found) and toasts what it gave; once by default |
| `LocationTrigger` | a trigger collider | `locationId`; entering → location banner (name + region chain), `OnLocationChanged`, optional scene load, and auto-plays unplayed **Scene** beats set there |
| `Signpost` | any object | reads a location's description as narration |
| `DialogueUI` | the canvas | portrait, name, emotion, typewriter body, ▼ prompt, choice list (locked choices greyed with the reason from the runner), keyboard + mouse |
| `LocationBanner`, `PickupToast`, `InventoryHUD` | the canvas | arrival banner, "Got X" toast, Tab inventory listing owned items with icons |
| `StoryMapUI` | the canvas | hold-M overlay: current beat + description, reachable next beats with lock / visited marks, discoverables here, recent beats, endings ✓/○, progress; `Pin(true)` keeps it open |
| `StoryloomDirector` | one per scene | owns the runner; `TalkTo`, `PlayNode`, `EnterLocation`, `Pickup`, `SaveJson/LoadJson`; UnityEvents `OnStoryEvent(name)`, `OnBeatStarted/Finished(node)`, `OnEndingReached(node)`, `OnLocationChanged(id)`, `OnItemGained/Lost(id)` |

**Pacing.** The director does not run the whole graph at once. It plays a beat until the next node is a *Dialogue* whose speakers aren't the NPC you're talking to ("…talk to Bram") or a node set at another location ("…at Lantern Road"); it remembers that node as `PendingNodeId`, and talking to that character or walking into that location resumes exactly there. Scenes, events, checks, random, unlocks and endings flow on their own. Both gates are toggles on `StoryloomDirector` (`gateDialogueByCharacter`, `gateByLocation`). With `strictOrder` (default on) beats also only become *available* in story order — an NPC whose beat hasn't been reached says they have nothing to say yet, a discoverable whose host beat hasn't played says "Nothing here yet", and an item that a later beat gives can't be grabbed early. Picking up an item plays the beat that gives it (`pickupPlaysGivingBeat`), so every effect on that beat applies, not just the inventory flag.

**Starting values.** The window's *Variables* tab sets this game's starting variable values and starting inventory (stored on the Bindings asset, applied whenever the runner resets; blank = the story's default).

How a beat plays: the director runs the graph from the chosen node — dialogue lines one by one (portrait + bark), scene text as narration, choices as buttons, checks and random silently, events fire `OnStoryEvent`, unlock effects apply — until an ending, a dead end, a discoverable's "back to where you found it", or (with *autoPlaySceneBeatsOnEnter* off) the next beat being a scene somewhere else, so the player walks there and the `LocationTrigger` continues the story.

Wire game logic to the UnityEvents in the inspector (e.g. `OnStoryEvent` → your quest manager, `OnItemGained` → animation), or subscribe in code: `StoryloomDirector.Instance.Runner.OnVariableChanged += …`.

## Should the kit exist, and where does it stop?

Yes: the export is data; a game needs the data *placed*. The kit's job is exactly the "assign the correct aspects so it's playable" step — bindings + a director + a handful of world components — and it stops short of being a game framework. It gives you a Stardew-shaped skeleton (top-down, talk/pick-up/examine, banners, dialogue box) that you can keep, retheme, or swap out component by component: replace `PlayerController2D` with your own controller and keep the interactables; replace `DialogueUI` with your dialogue system and keep the director; keep only the runner if you have everything else. Nothing in the kit is required by the runner.

Requirements: Unity 2021.3+, uGUI (legacy Text, no TextMeshPro), Input System package, 2D physics.

## JSON shape (abridged)

```json
{
  "format": "storyloom-unity", "version": 2, "name": "...", "startNodeId": "n1",
  "variables":  [{ "name": "gold", "type": "int", "defaultValue": "12" }],
  "characters": [{ "id": "c_bram", "name": "Warden Bram", "roleType": "antagonist", "role": "Gatekeeper", "speciesId": "s_human", "factionId": "f_watch", "homeLocationId": "l_gate",
                   "relationships": [{ "characterId": "c_wren", "kind": "acquaintance", "note": "" }], "description": "...", "traits": "...", "motivation": "...", "voice": "...", "voiceSamples": [], "image": "" }],
  "factions":   [{ "id": "f_watch", "name": "Ashford Watch", "kind": "military", "baseLocationId": "l_gate", "parentFactionId": "", "description": "...", "goals": "...", "image": "" }],
  "species":    [{ "id": "s_human", "name": "Human", "kind": "race", "description": "...", "traits": "", "image": "" }],
  "regions":    [{ "id": "r_ashford", "name": "Ashford", "kind": "town", "parentRegionId": "r_realm", "description": "...", "image": "" }],
  "locations":  [{ "id": "l_gate", "name": "Ashford Gate", "kind": "landmark", "regionId": "r_ashford", "region": "Ashford", "atmosphere": "...", "description": "...", "image": "" }],
  "items":      [{ "id": "...", "name": "...", "kind": "key item", "description": "...", "effect": "...", "startOwned": false, "image": "" }],
  "lore":       [{ "id": "lo_curfew", "name": "The dusk curfew", "kind": "rule", "description": "...", "image": "" }],
  "nodes": [{
    "id": "n3", "type": "choice", "title": "...", "text": "...",
    "speakerId": "", "locationId": "l_gate", "when": "Day 1 · Dusk", "why": "...", "jumpToNodeId": "",
    "lines": [{ "speakerId": "c_bram", "text": "...", "emotion": "weary" }],
    "characterIds": [], "factionIds": [], "itemIds": [], "speciesIds": [], "loreIds": [], "image": "",
    "conditionMode": "all", "conditions": [{ "variable": "gold", "op": ">=", "value": "10" }],
    "effects": [{ "variable": "gold", "op": "subtract", "value": "10" }],
    "options": [{ "id": "o_bribe", "label": "Slip him some coin", "conditionMode": "all", "conditions": [] }],
    "links":   [{ "port": "o_bribe", "toNodeId": "n4", "label": "", "conditionMode": "all", "conditions": [] }]
  }]
}
```

All fields are always present (empty string / empty array rather than null) so `JsonUtility` never sees a missing member.

## World & cast lookups (export version 2)

Variable types are `bool`, `int`, `float`, `string` (older exports say `number`; the runner treats it as float). `GetInt` / `GetFloat` / `GetNumber` / `GetBool` read them. Integers are truncated after `add` / `subtract`.

```csharp
var story = StoryloomStory.FromJson(json);
foreach (var c in story.CharactersByRole("antagonist")) Debug.Log(c.name);
foreach (var c in story.CharactersInFaction("f_watch", includeChildren: true)) Debug.Log(c.name);
var loc = story.GetLocation(node.locationId);
foreach (var r in story.RegionsOf(loc)) Debug.Log($"{r.kind}: {r.name}");   // village → country → world
var home = story.GetLocation(story.GetCharacter("c_wren").homeLocationId);
foreach (var id in node.loreIds) Debug.Log(story.GetLore(id).name);         // lore revealed at this node
```

## Discoverables (export version 2)

`type: "discoverable"` nodes are optional side content (secrets, side quests, collectibles, encounters) placed at a main-thread node via `hostNodeId`. They are not in the node's `links`; ask the runner:

```csharp
foreach (var d in runner.GetDiscoverables())          // at the current node
    Debug.Log($"{d.label}{(d.locked ? " (locked: " + d.lockReason + ")" : "")}{(d.found ? " [found]" : "")}");
runner.Choose(d);                                     // applies its effects; then GetOptions() gives its "Then" link, or a "Back to <host>" return option
```

`StoryVariable.tracked` / `Item.tracked` mirror the editor's 🔧 technical-review flag (informational).
