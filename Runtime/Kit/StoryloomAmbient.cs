// Storyloom Unity Kit — ambient life for generated scenes.
//   AmbientBarks   NPCs with idle lines speak them as small world-space bubbles when the player walks near — the same
//                  authored barks TalkTo uses, minus once-only lines (those are saved for a real conversation) and without
//                  consuming visit counts. One driver on the director object covers the whole scene.
//   ObjectiveHint  a one-line HUD element showing StoryloomDirector.ObjectiveText() — "→ Talk to Bram", "→ Go to the
//                  Harbour" — derived from the paused beat or the next available one. Hidden during beats and modals.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Storyloom
{
    public class AmbientBarks : MonoBehaviour
    {
        [Tooltip("How close the player must be before an NPC speaks up (world units, on the world plane)")]
        public float radius = 4f;
        [Tooltip("Seconds before the same NPC will speak ambiently again")]
        public float perNpcCooldown = 30f;
        [Tooltip("Minimum seconds between any two ambient lines scene-wide")]
        public float globalGap = 6f;
        public float bubbleSeconds = 3f;
        public bool enabledBarks = true;

        readonly Dictionary<string, float> _nextPerNpc = new Dictionary<string, float>();
        float _nextGlobal; Transform _bubble; TextMesh _tm; Coroutine _hide;

        void Update()
        {
            if (!enabledBarks) return;
            var d = StoryloomDirector.Instance; var p = StoryloomPlayer.Current;
            if (d == null || d.Story == null || p == null || d.InBeat || d.UiBusy) return;
            if (Time.time < _nextGlobal) return;
            foreach (var it in Interactable.All)
            {
                if (!(it is NpcInteractable npc) || !npc.gameObject.activeInHierarchy) continue;
                if (npc.DistanceTo(p.transform.position, p.UsesXZ) > radius) continue;
                if (_nextPerNpc.TryGetValue(npc.characterId, out var t) && Time.time < t) continue;
                var c = d.Story.GetCharacter(npc.characterId); if (c == null || c.barks == null || c.barks.Length == 0) continue;
                var visit = (d.TalkVisits.TryGetValue(npc.characterId, out var v) ? v : 0) + 1;
                var bark = d.PickBark(c, visit);
                if (bark == null || bark.once) { _nextPerNpc[npc.characterId] = Time.time + perNpcCooldown; continue; }   // once-only lines wait for a real talk
                _nextPerNpc[npc.characterId] = Time.time + perNpcCooldown; _nextGlobal = Time.time + globalGap;
                Show(npc.transform, c.name, bark.text, p.UsesXZ);
                StoryloomDirector.Note($"Ambient bark: {c.name}");
                d.Rec($"{c.name} (ambient): {bark.text}");
                return;   // one speaker at a time
            }
        }

        void Show(Transform over, string speaker, string text, bool xz)
        {
            if (!_bubble)
            {
                _bubble = new GameObject("Ambient bubble").transform;
                _tm = _bubble.gameObject.AddComponent<TextMesh>();
                _tm.characterSize = .055f; _tm.fontSize = 44; _tm.anchor = TextAnchor.LowerCenter; _tm.alignment = TextAlignment.Center; _tm.richText = true;
                _bubble.gameObject.AddComponent<Billboard>();
                var mr = _bubble.GetComponent<MeshRenderer>(); if (mr) mr.sortingOrder = 9;
            }
            _bubble.gameObject.SetActive(true);
            _bubble.position = over.position + (xz ? Vector3.up * 1.8f : new Vector3(0, 1.4f, -1f));
            _tm.text = "<color=#ffd94d>" + speaker + "</color>\n" + Wrap(text);
            if (_hide != null) StopCoroutine(_hide); _hide = StartCoroutine(HideAfter());
        }
        IEnumerator HideAfter() { yield return new WaitForSeconds(bubbleSeconds); if (_bubble) _bubble.gameObject.SetActive(false); }
        static string Wrap(string text)
        {
            const int at = 30; if (string.IsNullOrEmpty(text) || text.Length <= at) return text;
            var sb = new System.Text.StringBuilder(); int len = 0;
            foreach (var w in text.Split(' '))
            { if (len > 0 && len + w.Length + 1 > at) { sb.Append('\n'); len = 0; } else if (len > 0) { sb.Append(' '); len++; } sb.Append(w); len += w.Length; }
            return sb.ToString();
        }
        void OnDisable() { if (_bubble) _bubble.gameObject.SetActive(false); }
    }

    /// <summary>One-line objective HUD: what to do next, straight from the director's gating knowledge.</summary>
    public class ObjectiveHint : MonoBehaviour
    {
        public Text text; float _t;
        void Update()
        {
            _t += Time.unscaledDeltaTime; if (_t < 0.5f) return; _t = 0;
            var d = StoryloomDirector.Instance; if (!text) return;
            text.text = d != null && !d.InBeat && !d.UiBusy ? d.ObjectiveText() : "";
        }
    }
}
