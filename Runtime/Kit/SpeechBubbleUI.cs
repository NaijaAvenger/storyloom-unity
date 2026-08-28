// Storyloom Unity Kit — speech-bubble dialogue presenter (a complete IDialogueUI example).
// Lines appear as a billboarded text bubble floating over whoever is speaking (found via the NPC interactables; narration
// and unknown speakers float over the player). Choices render in the bubble as a list steered with the nav keys and
// confirmed with advance. To use: add SpeechBubbleUI to any GameObject and drag it into the director's
// "Dialogue Override" slot — the Stardew-style dialogue box is then ignored entirely. This is deliberately the smallest
// real implementation of the contract, meant to be copied as the starting point for your own presenter.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Storyloom
{
    public class SpeechBubbleUI : MonoBehaviour, IDialogueUI
    {
        [Header("Look")]
        public float characterSize = .06f;
        public int fontSize = 48;
        public Color textColor = Color.white, nameColor = new Color(1, .85f, .3f), lockedColor = new Color(1, 1, 1, .45f);
        [Tooltip("Approximate characters per line before wrapping")] public int wrapAt = 34;
        [Tooltip("Bubble height above the speaker's head (world units)")] public float lift = 1.9f;
        public float charsPerSecond = 45f;

        TextMesh _tm; Transform _holder;
        StoryloomKeyBinds K => StoryloomDirector.Instance ? StoryloomDirector.Instance.keys : null;

        // ---- IDialogueUI -------------------------------------------------------------------------------------------
        public IEnumerator Say(string speaker, string text, Sprite portrait, string emotion, AudioClip bark)
        {
            var target = FindSpeaker(speaker);
            if (bark) AudioSource.PlayClipAtPoint(bark, target ? target.position : Vector3.zero);
            string head = string.IsNullOrEmpty(speaker) ? "" : speaker + (string.IsNullOrEmpty(emotion) ? "" : " (" + emotion + ")") + "\n";
            yield return TypeOver(target, head, text);
            yield return WaitAdvance();
        }
        public IEnumerator Narrate(string title, string text, bool ending = false)
        {
            yield return TypeOver(PlayerTransform(), string.IsNullOrEmpty(title) ? "" : (ending ? "— " + title + " —" : title) + "\n", text);
            yield return WaitAdvance();
        }
        public IEnumerator Choose(List<StoryOption> options, Action<StoryOption> pick)
        {
            var target = PlayerTransform();
            int idx = 0; while (idx < options.Count && options[idx].locked) idx++;
            StoryOption chosen = null;
            while (chosen == null)
            {
                var sb = new StringBuilder();
                for (int i = 0; i < options.Count; i++)
                {
                    var o = options[i];
                    sb.Append(i == idx ? "▸ " : "   ").Append(Wrap(o.locked ? o.label + "  (locked: " + o.lockReason + ")" : o.label)).Append('\n');
                }
                Show(target, "", sb.ToString());
                if (K != null && K.NavDownDown()) { for (int n = 0; n < options.Count; n++) { idx = (idx + 1) % options.Count; if (!options[idx].locked) break; } }
                if (K != null && K.NavUpDown()) { for (int n = 0; n < options.Count; n++) { idx = (idx - 1 + options.Count) % options.Count; if (!options[idx].locked) break; } }
                if (K != null && K.AdvanceDown() && idx < options.Count && !options[idx].locked) chosen = options[idx];
                yield return null;
            }
            pick(chosen);
        }
        public void ShowBark(string speaker, string text, Sprite portrait) { StopAllCoroutines(); StartCoroutine(OneShot(FindSpeaker(speaker), string.IsNullOrEmpty(speaker) ? "" : speaker + "\n", text)); }
        public void ShowNarration(string title, string text) { StopAllCoroutines(); StartCoroutine(OneShot(PlayerTransform(), string.IsNullOrEmpty(title) ? "" : title + "\n", text)); }
        public void Hide() { if (_holder) _holder.gameObject.SetActive(false); }

        // ---- internals ---------------------------------------------------------------------------------------------
        IEnumerator OneShot(Transform target, string head, string text) { yield return TypeOver(target, head, text); yield return WaitAdvance(); Hide(); }
        IEnumerator TypeOver(Transform target, string head, string text)
        {
            text = Wrap(text ?? ""); float shown = 0; bool skip = false;
            while (shown < text.Length)
            {
                if (K != null && K.AdvanceDown()) skip = true;
                shown += skip ? 9999 : charsPerSecond * Time.deltaTime;
                Show(target, head, text.Substring(0, Mathf.Min(text.Length, Mathf.FloorToInt(shown))));
                yield return null;
            }
            Show(target, head, text + "  ▼");
        }
        IEnumerator WaitAdvance() { yield return null; while (K == null || !K.AdvanceDown()) yield return null; }

        void Show(Transform target, string head, string body)
        {
            if (!_holder)
            {
                _holder = new GameObject("Speech bubble").transform;
                _tm = _holder.gameObject.AddComponent<TextMesh>();
                _tm.anchor = TextAnchor.LowerCenter; _tm.alignment = TextAlignment.Center; _tm.richText = true;
                _holder.gameObject.AddComponent<Billboard>();
                var mr = _holder.GetComponent<MeshRenderer>(); if (mr) mr.sortingOrder = 10;
            }
            _holder.gameObject.SetActive(true);
            _tm.characterSize = characterSize; _tm.fontSize = fontSize; _tm.color = textColor;
            _tm.text = (string.IsNullOrEmpty(head) ? "" : "<color=#" + ColorUtility.ToHtmlStringRGB(nameColor) + ">" + head + "</color>") + body;
            var p = StoryloomPlayer.Current; bool xz = p == null || p.UsesXZ;
            var anchor = target ? target : PlayerTransform();
            if (anchor) _holder.position = anchor.position + (xz ? Vector3.up * lift : new Vector3(0, lift * .7f, -1f));
        }
        string Wrap(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= wrapAt) return text;
            var sb = new StringBuilder(); int lineLen = 0;
            foreach (var word in text.Split(' '))
            {
                if (lineLen > 0 && lineLen + word.Length + 1 > wrapAt) { sb.Append('\n'); lineLen = 0; }
                else if (lineLen > 0) { sb.Append(' '); lineLen++; }
                sb.Append(word); lineLen += word.Length;
            }
            return sb.ToString();
        }
        static Transform PlayerTransform() => StoryloomPlayer.Current ? StoryloomPlayer.Current.transform : null;
        /// <summary>The transform of whoever `speakerName` is: the NPC interactable bound to the character with that name.</summary>
        Transform FindSpeaker(string speakerName)
        {
            var d = StoryloomDirector.Instance;
            if (d != null && d.Story != null && !string.IsNullOrEmpty(speakerName))
            {
                string id = null;
                foreach (var c in d.Story.characters ?? new Character[0]) if (c.name == speakerName) { id = c.id; break; }
                if (id != null)
                    foreach (var it in Interactable.All)
                        if (it is NpcInteractable npc && npc.characterId == id && npc.gameObject.activeInHierarchy) return npc.transform;
            }
            return PlayerTransform();
        }
        void OnDisable() { if (_holder) _holder.gameObject.SetActive(false); }
    }
}
