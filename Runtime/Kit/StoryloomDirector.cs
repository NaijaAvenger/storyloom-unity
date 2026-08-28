// Storyloom Unity Kit — the director.
// One per scene (or DontDestroyOnLoad). Owns the StoryRunner, decides which node an interaction starts, drives the
// dialogue UI through a beat, applies inventory to the HUD, fires events, and moves between locations.
//
//   Director.Instance.TalkTo(characterId)         → finds the best unplayed beat for that NPC here and plays it
//   Director.Instance.PlayNode(nodeId)            → plays a specific beat (discoverables, cutscenes, triggers)
//   Director.Instance.EnterLocation(locationId)   → banner, ambience, optional scene load, auto-play scene beats
//   Director.Instance.Pickup(itemId)              → give item + toast
//   OnStoryEvent (UnityEvent<string>)             → wire quest starts, cutscenes, unlocks from Event nodes
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace Storyloom
{
    [Serializable] public class StringEvent : UnityEvent<string> { }
    [Serializable] public class NodeEvent : UnityEvent<StoryNode> { }

    public class StoryloomDirector : MonoBehaviour
    {
        public static StoryloomDirector Instance { get; private set; }

        [Header("Data")]
        public StoryloomBindings bindings;
        public StoryloomKeyBinds keys;

        [Header("UI (optional — the kit's Create Scene wires these)")]
        public DialogueUI dialogue;
        public LocationBanner banner;
        public PickupToast toast;
        public InventoryHUD inventoryHud;
        public StoryMapUI map;                       // hold M: where you are / what's next / endings reached

        [Header("Behaviour")]
        public bool playStartNodeOnLoad = true;      // play the story's start node when the scene starts
        public bool autoPlaySceneBeatsOnEnter = true;// entering a location plays unplayed Scene beats set there
        public bool loadScenesForLocations = false;  // use LocationBinding.sceneName when the story moves location
        public bool persistAcrossScenes = true;
        [Tooltip("A beat stops before a Dialogue node whose speakers don't include the character the player is talking to — the player must find that NPC")]
        public bool gateDialogueByCharacter = true;
        [Tooltip("A beat stops before a node set at another location — the player must walk there (LocationTrigger / NPCs continue it)")]
        public bool gateByLocation = true;
        [Tooltip("Beats only become available in story order: a beat can start once one of the beats leading into it has been played (or it is the start / the paused beat). Talking to an NPC too early gets a 'nothing to say yet'.")]
        public bool strictOrder = true;
        [Tooltip("Picking up an item in the world plays the story beat that gives it (a Discoverable or Unlock), so its text and every effect apply — not just the inventory flag")]
        public bool pickupPlaysGivingBeat = true;

        [Header("Events")]
        public StringEvent OnStoryEvent;            // Event nodes → eventName
        public NodeEvent OnBeatStarted, OnBeatFinished, OnEndingReached;
        public StringEvent OnLocationChanged;       // locationId
        public StringEvent OnItemGained, OnItemLost;

        public StoryRunner Runner { get; private set; }
        public StoryloomStory Story => bindings != null && bindings.story != null ? bindings.story.Story : null;
        public string CurrentLocationId { get; private set; } = "";       // where the *story* is
        public string PlayerLocationId { get; set; } = "";                // where the player's body is (from LocationTriggers)
        public bool InBeat { get; private set; }
        public HashSet<string> Played { get; } = new HashSet<string>();
        /// <summary>The next story node the flow stopped in front of (gated by character / location); talking to the right NPC or arriving at the place resumes it.</summary>
        public string PendingNodeId { get; private set; } = "";
        string _talkingTo = "";
        Item _pickupToast;   // item whose giving beat is playing; toast "Got X" when the beat ends

        // Rolling log of what the kit did (shown by the debug HUD, F1) — the quickest way to tell "nothing happened" from "happened but invisible".
        public static readonly List<string> Log = new List<string>();
        public static void Note(string msg) { Log.Add(Time.time.ToString("0.0") + "s  " + msg); if (Log.Count > 8) Log.RemoveAt(0); }
        /// <summary>Fill UI references that were left empty (older scenes, hand-built ones) from whatever is in the scene, and say so.</summary>
        public void ResolveUI()
        {
            var missing = new List<string>();
            if (!dialogue) { dialogue = FindObjectOfType<DialogueUI>(true); if (dialogue) missing.Add("dialogue"); }
            if (!toast) { toast = FindObjectOfType<PickupToast>(true); if (toast) missing.Add("toast"); }
            if (!inventoryHud) { inventoryHud = FindObjectOfType<InventoryHUD>(true); if (inventoryHud) missing.Add("inventoryHud"); }
            if (!banner) { banner = FindObjectOfType<LocationBanner>(true); if (banner) missing.Add("banner"); }
            if (!map) { map = FindObjectOfType<StoryMapUI>(true); if (map) missing.Add("map"); }
            if (missing.Count > 0) Debug.LogWarning("Storyloom: director was missing UI references, found them in the scene: " + string.Join(", ", missing));
            if (!toast) Debug.LogWarning("Storyloom: no PickupToast in the scene — 'Got X' popups can't show. Regenerate the scene from Window ▸ Storyloom.");
            if (!inventoryHud) Debug.LogWarning("Storyloom: no InventoryHUD in the scene — the inventory key can't open anything. Regenerate the scene from Window ▸ Storyloom.");
        }
        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            ResolveUI();
            if (persistAcrossScenes) DontDestroyOnLoad(gameObject);
            if (keys == null) keys = StoryloomKeyBinds.Default();
            if (Story == null) { Debug.LogError("Storyloom: assign a Bindings asset with a Story asset."); return; }
            Runner = new StoryRunner(Story);
            Runner.OnEvent += (name, n) => OnStoryEvent?.Invoke(name);
            Runner.OnEnding += n => OnEndingReached?.Invoke(n);
            // The HUD refresh runs inside GiveItem/TakeItem: an exception in there (a half-built inventory prefab, a missing row)
            // used to unwind through Pickup and the beat coroutine, so the item arrived but the "Got X" toast never fired.
            Runner.OnVariableChanged += (k, v) =>
            {
                if (!k.StartsWith(StoryRunner.ItemPrefix)) return;
                var id = k.Substring(StoryRunner.ItemPrefix.Length);
                if (v is bool b && b) OnItemGained?.Invoke(id); else OnItemLost?.Invoke(id);
                if (inventoryHud) { try { inventoryHud.Refresh(); } catch (Exception e) { Debug.LogError("Storyloom: inventory refresh failed — " + e); } }
            };
            Runner.ResetVariables(); bindings.ApplyStartingValues(Runner);
        }
        /// <summary>Reset variables and apply the editor's starting-value overrides.</summary>
        public void ResetStory() { Runner.ResetVariables(); bindings.ApplyStartingValues(Runner); Played.Clear(); PendingNodeId = ""; }

        void Start()
        {
            if (Runner == null) return;
            AutoRepairScene();
            if (playStartNodeOnLoad && Story.StartNode != null) PlayNode(Story.StartNode.id);
        }

        /// <summary>Generated objects are named "NPC · Name", "Item · Name", "Discoverable · Title", "Signpost · Location". If one lost its
        /// interactable or collider (older generated scenes, replaced prefabs, missing-script prefabs), put it back so the world stays playable.</summary>
        public void AutoRepairScene()
        {
            var fixedNames = new List<string>();
            var player = FindObjectOfType<StoryloomPlayer>(); bool xz = player && player.UsesXZ;   // 3D styles use 3D colliders
            foreach (var go in FindObjectsOfType<Transform>(true).Select(t => t.gameObject))
            {
                bool npc = go.name.StartsWith("NPC · "), item = go.name.StartsWith("Item · "), disc = go.name.StartsWith("Discoverable · "), sign = go.name.StartsWith("Signpost · ");
                if (!(npc || item || disc || sign)) continue;
                bool touched = false;
                // if (!go.GetComponent<Collider2D>()) { var c = go.AddComponent<BoxCollider2D>(); c.size = Vector2.one * .9f; touched = true; }
                // A prop from a prefab bound for the other style carries the other dimension's collider, and Unity will not hold
                // both — asking for the missing one logged "conflicts with the existing …" and handed back null. Switch it over.
                if (StoryloomColliders.MatchPlane(go, xz)) touched = true;
                if (!go.GetComponent<Interactable>())
                {
                    var nm = go.name.Substring(go.name.IndexOf('·') + 2); touched = true;
                    if (npc) { var ch = (Story.characters ?? new Character[0]).FirstOrDefault(x => x.name == nm); go.AddComponent<NpcInteractable>().characterId = ch != null ? ch.id : ""; }
                    else if (item) { var it = (Story.items ?? new Item[0]).FirstOrDefault(x => x.name == nm); go.AddComponent<ItemPickup>().itemId = it != null ? it.id : ""; }
                    else if (disc) { var n = Story.nodes.FirstOrDefault(x => x.IsDiscoverable && x.title == nm); go.AddComponent<DiscoverableInteractable>().nodeId = n != null ? n.id : ""; }
                    else { var l = (Story.locations ?? new Location[0]).FirstOrDefault(x => x.name == nm); go.AddComponent<Signpost>().locationId = l != null ? l.id : ""; }
                    var pr = go.transform.Find("Prompt"); var inter = go.GetComponent<Interactable>(); if (pr && inter) inter.prompt = pr.gameObject;
                }
                // reach is measured against the colliders, and this runs after their Awake cached them — re-read whatever we just added
                if (touched) { var inter2 = go.GetComponent<Interactable>(); if (inter2) inter2.CacheColliders(); fixedNames.Add(go.name); }
            }
            // var player = FindObjectOfType<PlayerController2D>();
            if (player && !xz && !player.GetComponent<Collider2D>()) { var pc = player.gameObject.AddComponent<CircleCollider2D>(); if (pc) { pc.radius = .4f; fixedNames.Add(player.name); } }
            if (player && !xz) { var prb = player.GetComponent<Rigidbody2D>(); if (prb) { prb.sleepMode = RigidbodySleepMode2D.NeverSleep; prb.interpolation = RigidbodyInterpolation2D.Interpolate; prb.collisionDetectionMode = CollisionDetectionMode2D.Continuous; } }
            // zones: must be triggers; in 3D they need a kinematic rigidbody for enter/exit against the CharacterController and sit on Ignore Raycast
            foreach (var z in FindObjectsOfType<LocationTrigger>(true))
            {
                var c3 = z.GetComponent<Collider>(); var c2 = z.GetComponent<Collider2D>(); bool t = false;
                if (c3 && !c3.isTrigger) { c3.isTrigger = true; t = true; }
                if (c2 && !c2.isTrigger) { c2.isTrigger = true; t = true; }
                if (c3 && xz && !z.GetComponent<Rigidbody>()) { var rb = z.gameObject.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false; t = true; }
                if (c3 && xz && z.gameObject.layer != 2) { z.gameObject.layer = 2; t = true; }
                if (t) fixedNames.Add(z.name + " (zone)");
            }
            if (fixedNames.Count > 0) Debug.LogWarning("Storyloom: repaired " + fixedNames.Count + " scene object(s) that were missing an interactable / collider: " + string.Join(", ", fixedNames) + ". Regenerate the scene (Window ▸ Storyloom) to make this permanent.");
        }

        // ------------------------------------------------------------------ choosing beats

        /// <summary>Best beat for talking to a character here: unplayed first, conditions satisfied, at this location if any beat is location-specific.</summary>
        public StoryNode BeatForCharacter(string characterId, IList<string> preferred = null)
        {
            if (preferred != null) foreach (var id in preferred) { var n = Story.GetNode(id); if (n != null && Ok(n)) return n; }
            var cands = Story.nodes.Where(n => !n.IsDiscoverable && Available(n) && (n.speakerId == characterId || (n.characterIds != null && Array.IndexOf(n.characterIds, characterId) >= 0) || (n.lines != null && n.lines.Any(l => l.speakerId == characterId)))).ToList();
            return Pick(cands);
        }
        /// <summary>Unplayed Scene beats placed at a location (played on entering, Stardew "you arrive and something happens").</summary>
        public IEnumerable<StoryNode> SceneBeatsAt(string locationId) => Story.nodes.Where(n => n.locationId == locationId && (n.type == "scene" || n.type == "event") && !Played.Contains(n.id) && Ok(n) && Available(n)).OrderBy(FlowIndex);

        StoryNode Pick(List<StoryNode> cands)
        {
            bool here(StoryNode n) => string.IsNullOrEmpty(CurrentLocationId) || string.IsNullOrEmpty(n.locationId) || n.locationId == CurrentLocationId;
            var ok = cands.Where(Ok).ToList();
            return ok.Where(n => here(n) && !Played.Contains(n.id)).OrderBy(FlowIndex).FirstOrDefault()
                ?? ok.Where(n => !Played.Contains(n.id)).OrderBy(FlowIndex).FirstOrDefault()
                ?? ok.Where(here).OrderBy(FlowIndex).FirstOrDefault()
                ?? ok.OrderBy(FlowIndex).FirstOrDefault();
        }
        bool Ok(StoryNode n) { if (n == null) return false; if (n.IsCheck) return true; return Runner.Evaluate(n.conditions, n.conditionMode, out _); }
        int FlowIndex(StoryNode n) => Array.IndexOf(Story.nodes, n);   // export order = reading order from Storyloom

        // ------------------------------------------------------------------ playing

        public void TalkTo(string characterId, IList<string> preferred = null)
        {
            if (InBeat) return;
            _talkingTo = characterId;
            StoryNode n = null;
            var pend = Story.GetNode(PendingNodeId);
            if (pend != null && Involves(pend, characterId) && Ok(pend)) n = pend;             // resume the story where it paused
            if (n == null) n = BeatForCharacter(characterId, preferred);
            if (n == null)
            {
                var c = Story.GetCharacter(characterId); var nm = c != null ? c.name : "?";
                bool hasLater = strictOrder && Story.nodes.Any(x => !x.IsDiscoverable && !Played.Contains(x.id) && Involves(x, characterId));   // they have lines, just not yet
                if (dialogue) dialogue.ShowBark(nm, hasLater ? "…" + nm + " has nothing to say to you yet." : "...", Portrait(characterId));
                return;
            }
            PlayNode(n.id);
        }

        /// <summary>Beats that lead into `n`: links, jumps, and (for discoverables) the host.</summary>
        IEnumerable<StoryNode> Predecessors(StoryNode n)
        {
            foreach (var p in Story.nodes)
            {
                if (p.links != null && p.links.Any(l => l.toNodeId == n.id)) yield return p;
                else if (p.IsJump && p.jumpToNodeId == n.id) yield return p;
            }
            if (n.IsDiscoverable && !string.IsNullOrEmpty(n.hostNodeId)) { var h = Story.GetNode(n.hostNodeId); if (h != null) yield return h; }
        }
        /// <summary>Is this beat reachable *now* in story order? Start node, the paused beat, anything already played (replays), or a beat whose predecessor has been played.</summary>
        public bool Available(StoryNode n)
        {
            if (n == null) return false;
            if (!strictOrder) return true;
            if (n.id == PendingNodeId || Played.Contains(n.id) || (Story.StartNode != null && n.id == Story.StartNode.id) || n.entry) return true;   // entry points can always be started
            if (Runner.Current != null && Runner.Current.id == n.id) return true;
            return Predecessors(n).Any(p => Played.Contains(p.id) || p.id == PendingNodeId);
        }
        static bool Involves(StoryNode n, string characterId) => !string.IsNullOrEmpty(characterId) && (n.speakerId == characterId || (n.characterIds != null && Array.IndexOf(n.characterIds, characterId) >= 0) || (n.lines != null && n.lines.Any(l => l.speakerId == characterId)));
        bool IsPlayer(string characterId) { var c = Story.GetCharacter(characterId); return c != null && c.IsProtagonist; }
        /// <summary>Should the flow pause before `target` instead of playing it now?</summary>
        bool Gated(StoryNode target, out string why)
        {
            why = "";
            if (target == null) return false;
            if (gateByLocation && !string.IsNullOrEmpty(target.locationId) && target.locationId != CurrentLocationId && !target.IsDiscoverable) { why = "at " + (Story.GetLocation(target.locationId)?.name ?? target.locationId); return true; }
            if (gateDialogueByCharacter && target.IsDialogue)
            {
                // who does the player need to be talking to? every non-player speaker in the node
                var speakers = new HashSet<string>(); if (!string.IsNullOrEmpty(target.speakerId)) speakers.Add(target.speakerId); if (target.lines != null) foreach (var l in target.lines) if (!string.IsNullOrEmpty(l.speakerId)) speakers.Add(l.speakerId);
                speakers.RemoveWhere(IsPlayer);
                if (speakers.Count > 0 && !speakers.Contains(_talkingTo)) { why = "talk to " + string.Join(" or ", speakers.Select(id => Story.GetCharacter(id)?.name ?? id)); return true; }
            }
            return false;
        }

        public void PlayNode(string nodeId)
        {
            if (InBeat) return;
            var n = Story.GetNode(nodeId); if (n == null) { Debug.LogWarning($"Storyloom: no node {nodeId}"); return; }
            if (n.IsDialogue && string.IsNullOrEmpty(_talkingTo) && !string.IsNullOrEmpty(n.speakerId)) _talkingTo = n.speakerId;
            StartCoroutine(RunBeat(n));
        }

        /// <summary>Plain-language summary of what a node does when reached: "gold +5 · got Brass Lantern · met Bram".</summary>
        public static string RewardSummary(StoryloomStory story, StoryNode n)
        {
            if (n == null || n.effects == null || n.effects.Length == 0) return "";
            var parts = new List<string>();
            foreach (var e in n.effects)
            {
                if (string.IsNullOrEmpty(e.variable)) continue;
                if (e.variable.StartsWith(StoryRunner.ItemPrefix)) { var it = story.GetItem(e.variable.Substring(StoryRunner.ItemPrefix.Length)); var nm = it != null ? it.name : e.variable; parts.Add(e.op == "take" ? "lost " + nm : "got " + nm); continue; }
                switch (e.op) { case "add": parts.Add($"{e.variable} +{e.value}"); break; case "subtract": parts.Add($"{e.variable} −{e.value}"); break; case "toggle": parts.Add($"{e.variable} toggled"); break; default: parts.Add($"{e.variable} = {e.value}"); break; }
            }
            return string.Join(" · ", parts);
        }
        public string RewardSummary(StoryNode n) => RewardSummary(Story, n);
        /// <summary>Requirements a node needs before it can be found, or "" when open.</summary>
        public string LockReason(StoryNode n) => n != null && !Runner.Evaluate(n.conditions, n.conditionMode, out _) ? Runner.Reason(n.conditions, n.conditionMode) : "";

        // ---- beat history: a snapshot of the full story state before each beat, so playtesting can rewind ------------
        [Serializable] public class BeatRecord { public string nodeId, title; public string stateJson; public float time; }
        /// <summary>One entry per beat played this session, oldest first; each holds the state from *before* that beat.</summary>
        public readonly List<BeatRecord> History = new List<BeatRecord>();
        /// <summary>Restore the story to the moment before `record`'s beat played. Story state (variables, inventory, played
        /// set, pending, location) is fully restored; world objects a pickup destroyed stay gone (deactivated ones return).</summary>
        public void RewindTo(BeatRecord record)
        {
            if (record == null || InBeat) return;
            LoadJson(record.stateJson);
            int i = History.IndexOf(record); if (i >= 0) History.RemoveRange(i, History.Count - i);
            foreach (var p in FindObjectsOfType<ItemPickup>(true))                       // un-picked-up items come back
                if (!p.gameObject.activeSelf && Runner != null && !Runner.HasItem(p.itemId)) p.gameObject.SetActive(true);
            if (inventoryHud) inventoryHud.Refresh();
            Note($"Rewound to before '{record.title}' ({Played.Count} played)");
        }

        IEnumerator RunBeat(StoryNode first)
        {
            History.Add(new BeatRecord { nodeId = first.id, title = string.IsNullOrEmpty(first.title) ? first.id : first.title, stateJson = SaveJson(), time = Time.time });
            if (History.Count > 200) History.RemoveAt(0);
            InBeat = true; if (first.id == PendingNodeId) PendingNodeId = "";
            bool wasDiscoverable = first.IsDiscoverable; string reward = wasDiscoverable ? RewardSummary(first) : "";
            Runner.GoTo(first.id);
            while (true)
            {
                var n = Runner.Current; Played.Add(n.id);
                OnBeatStarted?.Invoke(n);
                if (!string.IsNullOrEmpty(n.locationId) && n.locationId != CurrentLocationId && n.type != "discoverable") SetLocation(n.locationId, false);

                if (n.IsDialogue && n.lines != null && n.lines.Length > 0)
                {
                    foreach (var l in n.lines)
                    {
                        var c = Story.GetCharacter(l.speakerId);
                        yield return dialogue ? dialogue.Say(c != null ? c.name : "", l.text, Portrait(l.speakerId), l.emotion, Bark(l.speakerId)) : null;
                    }
                }
                else if (n.IsEvent) { /* fired by the runner already */ if (!string.IsNullOrEmpty(n.text) && dialogue) yield return dialogue.Narrate(n.title, n.text); }
                else if (n.IsCheck || n.IsRandom || n.IsJump) { /* silent pass-through */ }
                else if (!string.IsNullOrEmpty(n.text) || n.IsEnding)
                {
                    var speaker = Story.GetCharacter(n.speakerId);
                    if (dialogue) yield return n.IsDialogue && speaker != null ? dialogue.Say(speaker.name, n.text, Portrait(n.speakerId), "", null) : dialogue.Narrate(n.title, n.text, n.IsEnding);
                }

                if (n.IsEnding) { OnBeatFinished?.Invoke(n); break; }

                // where next
                StoryOption next = null;
                if (n.IsRandom) next = Runner.PickRandom();
                else
                {
                    var opts = Runner.GetOptions();
                    if (opts.Count == 0) { OnBeatFinished?.Invoke(n); break; }             // dead end / discoverable return handled by runner
                    if (n.IsChoice && dialogue) { yield return dialogue.Choose(opts, o => next = o); }
                    else next = opts.FirstOrDefault(o => !o.locked);
                }
                if (next == null) { OnBeatFinished?.Invoke(n); break; }
                var target = next.target;
                // a discoverable's "back to host" return ends the beat without replaying the host
                if (next.isReturn) { OnBeatFinished?.Invoke(n); break; }
                // pause here if the next beat belongs to another character or another place — the world continues it
                if (Gated(target, out var why)) { PendingNodeId = target.id; if (dialogue && !string.IsNullOrEmpty(why)) yield return dialogue.Narrate("", "…" + why + ".", false); OnBeatFinished?.Invoke(n); break; }
                PendingNodeId = "";
                Runner.Choose(next);
                // stop at natural pauses: the next beat is a scene at another location (walk there) — Stardew style
                if (target != null && !string.IsNullOrEmpty(target.locationId) && target.locationId != CurrentLocationId && target.type == "scene" && !autoPlaySceneBeatsOnEnter) { OnBeatFinished?.Invoke(n); break; }
            }
            if (dialogue) dialogue.Hide();
            InBeat = false;
            if (_pickupToast != null) { var pb = bindings.Item(_pickupToast.id); if (!toast) ResolveUI(); Note($"Beat ended: toast for {_pickupToast.name} {(toast ? "shown" : "MISSING")}, owned now {Runner.Inventory().Count()}"); if (toast) toast.Show($"Got {_pickupToast.name}" + (string.IsNullOrEmpty(reward) ? "" : " · " + reward), pb != null ? pb.icon : null); _pickupToast = null; }
            else if (wasDiscoverable && toast && !string.IsNullOrEmpty(reward)) toast.Show(reward, null);   // what the discoverable gave / did
            // the flow paused for a place the player is already standing in → continue there (dialogue still waits for its NPC)
            var pend2 = Story.GetNode(PendingNodeId);
            if (pend2 != null && !pend2.IsDialogue && !string.IsNullOrEmpty(pend2.locationId) && pend2.locationId == PlayerLocationId && Ok(pend2)) { yield return new WaitForSeconds(0.4f); if (!InBeat) { SetLocation(pend2.locationId, false); PlayNode(pend2.id); } }
        }

        // ------------------------------------------------------------------ world hooks

        // public void EnterLocation(string locationId) { SetLocation(locationId, true); }
        string _lastBannerLoc = "";
        /// <summary>The player walked into a zone. The banner shows every time the player's zone changes — even when the story already
        /// "was" there (a beat set the location before the player arrived), which used to swallow the popup.</summary>
        public void EnterLocation(string locationId)
        {
            if (locationId != _lastBannerLoc)
            {
                _lastBannerLoc = locationId;
                var loc = Story.GetLocation(locationId); var b = bindings.Location(locationId);
                if (!banner) ResolveUI();
                if (banner && loc != null) banner.Show(loc.name, RegionLine(loc), b != null ? b.banner : null, LocationBlurb(loc));
                Note("Zone: entered " + (loc != null ? loc.name : locationId) + (banner ? "" : " (NO BANNER IN SCENE)"));
            }
            else Note("Zone: re-entered " + locationId + " (banner already showing for it)");
            SetLocation(locationId, true);
        }
        /// <summary>The player walked out of a zone. Clearing the banner latch here is what lets the popup fire again when they come
        /// back: the latch used to be sticky, so A → B → A showed nothing on the return, and a story beat that pre-set the location
        /// swallowed the arrival popup entirely.</summary>
        public void ExitLocation(string locationId)
        {
            if (PlayerLocationId == locationId) PlayerLocationId = "";
            if (_lastBannerLoc == locationId) _lastBannerLoc = "";
            Note("Zone: left " + locationId);
        }
        void SetLocation(string locationId, bool fromWorld)
        {
            if (locationId == CurrentLocationId) { if (fromWorld && !InBeat) ResumeHere(locationId); return; }
            _talkingTo = "";
            CurrentLocationId = locationId;
            var loc = Story.GetLocation(locationId); var b = bindings.Location(locationId);
            // A story beat moving the location shows the banner but deliberately does *not* claim the latch: the latch belongs to
            // where the player's body is, so walking there afterwards still gets its own arrival popup.
            if (!fromWorld && banner && loc != null) banner.Show(loc.name, RegionLine(loc), b != null ? b.banner : null, LocationBlurb(loc));
            OnLocationChanged?.Invoke(locationId);
            if (loadScenesForLocations && b != null && !string.IsNullOrEmpty(b.sceneName) && SceneManager.GetActiveScene().name != b.sceneName) { SceneManager.LoadScene(b.sceneName); return; }
            if (fromWorld && !InBeat) ResumeHere(locationId);
        }
        // a paused beat for this place continues when the player arrives; otherwise an unplayed scene beat set here may auto-play
        void ResumeHere(string locationId)
        {
            var pend = Story.GetNode(PendingNodeId); if (pend != null && pend.locationId == locationId && !pend.IsDialogue && Ok(pend)) { PlayNode(pend.id); return; }
            if (autoPlaySceneBeatsOnEnter) { var beat = SceneBeatsAt(locationId).FirstOrDefault(); if (beat != null) PlayNode(beat.id); }
        }
        /// <summary>The description shown in the arrival popup: description, else atmosphere.</summary>
        public static string LocationBlurb(Location loc) { if (loc == null) return ""; var t = string.IsNullOrEmpty(loc.description) ? loc.atmosphere : loc.description; return t ?? ""; }
        string RegionLine(Location loc) { var parts = Story.RegionsOf(loc).Select(r => r.name).ToList(); return parts.Count > 0 ? string.Join(" · ", parts) : loc.kind; }

        /// <summary>The Discoverable / Unlock beat whose effects give this item, if any.</summary>
        public StoryNode GivingBeat(string itemId) => Story.nodes.FirstOrDefault(n => (n.IsDiscoverable || n.type == "unlock") && n.effects != null && n.effects.Any(e => e.op == "give" && e.variable == StoryRunner.ItemPrefix + itemId));
        public void Pickup(string itemId)
        {
            var it = Story.GetItem(itemId); if (it == null) { Note("Pickup: unknown item id " + itemId); return; }
            var beat = pickupPlaysGivingBeat ? GivingBeat(itemId) : null;
            if (beat != null && !Played.Contains(beat.id) && !InBeat && Available(beat) && Ok(beat))
            {
                _pickupToast = it; Note($"Pickup {it.name}: playing its giving beat '{beat.title}' (toast after)");
                PlayNode(beat.id);                                   // text + all effects (gold, flags, the item) through the runner
                if (InBeat || _pickupToast == null) return;          // running (toast fires when it ends), or it already ran and toasted
                _pickupToast = null; Note($"Pickup {it.name}: giving beat did not start — granting the item directly");
            }
            Runner.GiveItem(itemId);
            var b = bindings.Item(itemId);
            if (!toast) ResolveUI();
            Note($"Pickup {it.name}: gave item, toast {(toast ? "shown" : "MISSING")}, owned now {Runner.Inventory().Count()}");
            if (toast) toast.Show($"Got {it.name}", b != null ? b.icon : null);
        }

        public Sprite Portrait(string characterId)
        {
            var b = bindings.Character(characterId); if (b != null && b.portrait != null) return b.portrait;
            var c = Story.GetCharacter(characterId); var tex = c != null ? StoryloomImages.ToTexture(c.image) : null;
            return tex ? Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(.5f, .5f)) : null;
        }
        AudioClip Bark(string characterId) { var b = bindings.Character(characterId); return b != null ? b.voiceBark : null; }

        // ------------------------------------------------------------------ save / load
        public string SaveJson() { var st = Runner.SnapshotState(); return JsonUtility.ToJson(new SaveBlob { runner = st, played = Played.ToList(), location = CurrentLocationId, pending = PendingNodeId }); }
        public void LoadJson(string json) { var b = JsonUtility.FromJson<SaveBlob>(json); Runner.RestoreState(b.runner); Played.Clear(); foreach (var p in b.played) Played.Add(p); CurrentLocationId = b.location; PendingNodeId = b.pending ?? ""; if (inventoryHud) inventoryHud.Refresh(); }
        [Serializable] class SaveBlob { public StoryRunnerState runner; public List<string> played; public string location; public string pending; }
    }
}
