# Changelog

## 0.4.5
- Zone arrival popup: a strip across the top of the screen in every style — location name, region and the location's description — held for a few seconds (longer for longer text) and faded out. Signposts show the same popup instead of opening the dialogue box. Location description/atmosphere written in Storyloom's World tab is what appears.
- Repair open scene now REBUILDS missing UI (inventory HUD, pickup toast, banner, dialogue, story map, help, crosshair) into the existing canvas — this is the fix for older scenes where Tab and pickup popups silently did nothing because those components never existed (the F1 HUD showed "inventory hud: MISSING · toast: MISSING"). It also attaches NPC/item/discoverable components to objects whose floating label matches a cast/item/discoverable name, even if the object was renamed.
- Third-person camera: over-the-shoulder framing (shoulder offset), mouse/right-stick control whenever free-roaming (no longer requires the pointer lock that made it look static), pitch clamped, no positional lag.

## 0.4.4
- Zones: every LocationTrigger polls whether the player is inside it in 2D as well as 3D (physics trigger events are a bonus, not a requirement) — fixes top-down / third-person zones that never fired. The arrival banner shows every time the player's zone changes, even when the story had already moved there through a beat.
- Focus: the nearest interactable in reach wins (facing only breaks ties); reach is a little larger. Signposts always have something to say.
- Third-person camera: no positional lag, lower look scale, pitch clamped — no more swimming while turning.
- Lanes are 18 apart (7 units of open ground between zones); the ground grows with the number of locations.

## 0.4.3
- 3D scenes: location zones are their own trigger objects on the Ignore Raycast layer with a kinematic rigidbody (reliable enter/exit against the CharacterController; rays never hit them), and LocationTrigger also polls containment so a zone can't be missed. Per-location floors no longer carry colliders (the ground does), so nothing snags.
- First person: anything within reach that isn't behind you can be focused (not only what the centre ray hits).
- Self-test reports physics sanity: interactables missing the right collider type, zone collider state, interactables placed within 1.2 units of each other (focus flip-flops), the player's body. Repair (editor and runtime) fixes zone triggers / rigidbodies / layers.

## 0.4.2
- Diagnostics for "no pickup popup / inventory key does nothing / no [E] above NPCs": the director keeps a rolling log (shown in the F1 HUD: focus changes, interact / inventory presses and what they did, pickups and whether the toast was shown), the HUD shows whether toast / inventory / dialogue references exist and the active binds, and the window has **Self-test (play mode)** which fires the toast, toggles the inventory and forces the nearest prompt on, then prints the result to the Console.
- The director auto-finds missing UI references in the scene (older / hand-built scenes) and warns; Interactables build an [E] prompt themselves if the prefab has none; a second inventory key (I) alongside Tab.

## 0.4.1
- Entry points: exports carry entryNodeIds and node.entry; under strict order an entry-point beat is always available, so several quests / starting points can live on one board.

## 0.4.0
- Game styles: the window builds a top-down (Stardew), third-person or first-person test scene from the same story (Bindings.gameStyle + "Create test scene"). New runtime: StoryloomPlayer (base for all players), PlayerController3D + ThirdPersonCamera, FirstPersonController + Crosshair, CursorLock, Billboard, StoryloomPlaceholder marker; key binds gain LookAxis() (mouse / right stick), mouseSensitivity, stickSensitivity, invertY.
- Interactables take any StoryloomPlayer (Interact(StoryloomPlayer) / OnInteract(StoryloomPlayer) — update custom subclasses that overrode OnInteract(PlayerController2D)); Interactable.Nearest has an XZ overload; LocationTrigger works with 2D or 3D trigger colliders.
- Per-style placeholder prefabs (3D ones keep their colliders and billboard their labels); Repair and runtime self-repair are style-aware; debug HUD shows the style.

## 0.3.8
- "Got X" toast returns when a pickup plays its giving beat (shown when the beat ends, with the reward summary).
- Inventory toggle / refresh are defensive and log their failure reason; HUD shows inventory state and owned count.

## 0.3.7
- Strict story order (director.strictOrder): NPC beats, scene beats, discoverables and item pickups are only available once a beat leading into them has been played (or they are the start / paused beat). Early NPCs say they have nothing to say yet.
- Picking up an item plays the Discoverable / Unlock beat that gives it, so its text and every effect (gold, flags, the item) go through the runner (director.pickupPlaysGivingBeat).
- Inventory rows: bold name + wrapped description, rows size to their text.

## 0.3.6
- Director repairs generated objects at runtime (missing interactable / collider on NPC · / Item · / Discoverable · / Signpost · objects, player collider) and logs what it fixed.
- Debug HUD lists the registered interactables by name. Window shows the current key binds with Reset to defaults.

## 0.3.5
- Debug HUD (F1) on the director: registered interactables, nearest + distance, focus, beat state, story/player location, pending node.
- Interactables register in Awake and OnEnable; focus = nearest registered within reach, with a physics fallback.
- Window ▸ Repair open scene: re-adds missing colliders / interactable components / prompts on generated objects and the player.
- Labels and prompts sit in front of the mesh (z −1).

## 0.3.4
- Interaction focus no longer depends on 2D colliders / layers: interactables register themselves and the player picks the nearest in reach. Pressing E with nothing in reach says so.
- The player spawns at the start node's location; a paused beat resumes automatically if the player is already standing in its location; LocationTriggers report player presence (PlayerLocationId).

## 0.3.3
- Key-binds assets created before 0.3.0 stored KeyCode values; they are now converted to Input System keys automatically (this is why WASD / E / Tab stopped working and the help line showed F20F4F16F7). Help line reads "gamepad connected" only when one is.

## 0.3.2
- Input works under either Active Input Handling: Input System when active (gamepad), legacy Input Manager fallback otherwise, with a warning in the window and the console.
- Flow gating: a beat pauses before a Dialogue node whose speakers aren't the NPC the player is talking to, and before any node set at another location; talking to the right character / arriving there resumes the story from that node (PendingNodeId). Toggles on the director.
- Variables tab: edit starting values and starting inventory per game (stored on the Bindings asset, applied on reset).

## 0.3.1
- Discoverables are placed at their own / host's / nearest upstream location (Backstage only when the story gives no clue), labelled in-world with kind + reward ("secret · gold +5"), show their lock reason in the prompt, refuse politely when locked, and toast what they gave when found. Validate tab lists placement and rewards.

## 0.3.0
- Input System package instead of legacy Input: StoryloomKeyBinds now holds `Key` fields and builds InputActions at runtime (keyboard, mouse, gamepad); EventSystem uses InputSystemUIInputModule. Set Project Settings ▸ Player ▸ Active Input Handling to *Input System Package* or *Both*.

## 0.2.2
- Story map overlay (hold M): where you are, what's next with locks, recent beats, endings reached, progress; shows ALL ROUTES COMPLETED when every reachable ending has been seen (and pins itself at an ending).
- Dialogue choices no longer pre-highlight the first option; arrow keys start the highlight.
- Inventory rows stretch to the panel width.

## 0.2.1
- Placeholders are lit 3D primitives (capsule NPCs / player, cube items, sphere discoverables, quad floors) with pipeline-aware materials and a directional light; 2D physics kept.
- Dialogue choices: full-width buttons in the lower half of the box, sized to fit; body text keeps the upper half while choosing.
- Fix CS0819 in the editor window.

## 0.2.0
- Editor kit: Window ▸ Storyloom — import story JSON, bindings (characters / items / locations / discoverables → prefabs, sprites, scenes), validation, placeholder prefabs, one-click Stardew-style scene.
- Runtime kit: StoryloomDirector, PlayerController2D, NpcInteractable, ItemPickup, DiscoverableInteractable, LocationTrigger, Signpost, DialogueUI, LocationBanner, PickupToast, InventoryHUD, StoryloomKeyBinds, SimpleFollow.
- Export v2 support: species, regions, lore, relationships, discoverables (hostNodeId), tracked variables, int/float variable types.

## 0.1.0
- Runtime data classes + StoryRunner (choices, checks, random, jumps, events, inventory, save/load).
