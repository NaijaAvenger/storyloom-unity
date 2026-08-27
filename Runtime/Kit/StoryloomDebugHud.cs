// Storyloom Unity Kit — debug HUD (F1). Shows what the kit sees: registered interactables, the nearest one and its distance,
// the focused one, whether a beat is running, the story / player locations and the pending node. Use it to tell focus problems
// from flow problems. Added by "Create Stardew-style scene"; remove it from shipping builds.
using UnityEngine;
using System.Linq;

namespace Storyloom
{
    public class StoryloomDebugHud : MonoBehaviour
    {
        public bool show = true; public KeyCode legacyToggle = KeyCode.F1;
        GUIStyle _st; LocationTrigger[] _zones; string _zoneLine = ""; float _nextZoneScan;
        void Update()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current; if (kb != null && kb.f1Key.wasPressedThisFrame) show = !show;
#else
            if (Input.GetKeyDown(legacyToggle)) show = !show;
#endif
            // which zones geometrically contain the player, regardless of whether their trigger fired — the quickest way to tell
            // "the volume is in the wrong place" from "the physics callback went missing"
            if (!show || Time.unscaledTime < _nextZoneScan) return; _nextZoneScan = Time.unscaledTime + 0.25f;
            if (_zones == null || _zones.Length == 0 || System.Array.Exists(_zones, z => z == null)) _zones = FindObjectsOfType<LocationTrigger>();
            var p = StoryloomPlayer.Current;
            var inside = p ? _zones.Where(z => z && z.Contains(p)).Select(z => z.locationId).ToArray() : new string[0];
            _zoneLine = $"zones: {_zones.Length}  you are geometrically inside: {(inside.Length > 0 ? string.Join(", ", inside) : "—")}";
        }
        void OnGUI()
        {
            if (!show) return;
            if (_st == null) { _st = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 12, wordWrap = true }; _st.normal.textColor = Color.white; }
            var d = StoryloomDirector.Instance; var p = StoryloomPlayer.Current ? StoryloomPlayer.Current : FindObjectOfType<StoryloomPlayer>();
            int live = Interactable.All.Count(i => i != null && i.enabled && i.gameObject.activeInHierarchy);
            string s = $"Storyloom debug (F1)  ·  input: {StoryloomKeyBinds.Backend}  ·  style: {(p ? p.Style.ToString() : "?")}\n";
            s += $"interactables registered: {Interactable.All.Count} (live {live}): {string.Join(", ", Interactable.All.Where(i => i != null).Take(8).Select(i => i.name.Replace("NPC · ", "").Replace("Item · ", "").Replace("Discoverable · ", "").Replace("Signpost · ", "")))}{(Interactable.All.Count > 8 ? " …" : "")}\n";
            if (p) s += $"nearest: {(string.IsNullOrEmpty(p.NearestName) ? "—" : p.NearestName)}  dist {p.NearestDistance:0.00}  reach {p.Reach:0.00}\nfocus: {(p.Focus ? p.Focus.name : "—")}\n";
            else s += "no Storyloom player in scene\n";
            if (d) s += $"inventory hud: {(d.inventoryHud ? (d.inventoryHud.IsOpen ? "open" : "closed") : "MISSING")}  toast: {(d.toast ? "ok" : "MISSING")}  dialogue: {(d.dialogue ? "ok" : "MISSING")}  items owned: {(d.Runner != null ? d.Runner.Inventory().Count() : 0)}\n";
            if (d) s += $"director: {(d.Runner != null ? "ok" : "NO RUNNER")}  inBeat {d.InBeat}  story@ {d.CurrentLocationId}  player@ {d.PlayerLocationId}  pending {(string.IsNullOrEmpty(d.PendingNodeId) ? "—" : d.PendingNodeId)}  played {d.Played.Count}";
            else s += "no StoryloomDirector";
            if (!string.IsNullOrEmpty(_zoneLine)) s += "\n" + _zoneLine;
            if (p && p.keys) s += $"\nbinds: interact {p.keys.interact}/{p.keys.interactAlt}  inventory {p.keys.inventory}/{p.keys.inventoryAlt}  cursor {Cursor.lockState}";
            s += "\n— log —\n" + (StoryloomDirector.Log.Count == 0 ? "(nothing yet: walk to something, press the interact key)" : string.Join("\n", StoryloomDirector.Log));
            GUI.Box(new Rect(8, 8, 660, 170 + 15 * (StoryloomDirector.Log.Count + 1)), s, _st);
        }
    }
}
