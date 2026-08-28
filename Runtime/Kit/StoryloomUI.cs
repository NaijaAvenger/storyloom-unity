// Storyloom Unity Kit — uGUI widgets (legacy UI Text/Image, no TextMeshPro dependency).
//   DialogueUI     Stardew-style box: portrait, name, typewriter text, advance prompt, choice list (locked choices greyed with the reason)
//   LocationBanner fades a location name + region line in and out when the player arrives
//   PickupToast    "Got Brass Lantern" with icon
//   InventoryHUD   list of owned items (Tab)
// The editor window's "Create Stardew-style scene" builds all of these; you can restyle them freely — only the field references matter.
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Storyloom
{
    public class DialogueUI : MonoBehaviour, IDialogueUI
    {
        public GameObject panel;
        public Image portrait;
        public Text nameText, bodyText, emotionText, promptText;
        public Transform choicesParent;
        public Button choiceButtonPrefab;
        public AudioSource audioSource;
        [Header("Feel")] public float charsPerSecond = 45f; public bool narrationItalic = true;
        public Color lockedColor = new Color(1, 1, 1, .45f);

        StoryloomKeyBinds K => StoryloomDirector.Instance ? StoryloomDirector.Instance.keys : null;
        bool _skip;

        void Awake() { if (panel) panel.SetActive(false); }

        public IEnumerator Say(string speaker, string text, Sprite face, string emotion, AudioClip bark)
        {
            Open(); SetPortrait(face); if (nameText) nameText.text = speaker; if (emotionText) emotionText.text = string.IsNullOrEmpty(emotion) ? "" : $"({emotion})";
            if (bodyText) bodyText.fontStyle = FontStyle.Normal;
            if (bark && audioSource) audioSource.PlayOneShot(bark);
            yield return Type(text);
            yield return WaitAdvance();
        }
        public IEnumerator Narrate(string title, string text, bool ending = false)
        {
            Open(); SetPortrait(null); if (nameText) nameText.text = ending ? "— " + title + " —" : title; if (emotionText) emotionText.text = "";
            if (bodyText) bodyText.fontStyle = narrationItalic ? FontStyle.Italic : FontStyle.Normal;
            yield return Type(text);
            yield return WaitAdvance();
        }
        /// <summary>Show a one-liner (NPC with nothing to say, "nothing more here") and close on the next press.</summary>
        public void ShowBark(string speaker, string text, Sprite face) { StopAllCoroutines(); StartCoroutine(Bark(speaker, text, face)); }
        /// <summary>Narration that closes itself (signposts, examine text).</summary>
        public void ShowNarration(string title, string text) { StopAllCoroutines(); StartCoroutine(NarrateThenHide(title, text)); }
        IEnumerator NarrateThenHide(string title, string text) { yield return Narrate(title, text); Hide(); }
        IEnumerator Bark(string speaker, string text, Sprite face) { yield return Say(speaker, text, face, "", null); Hide(); }

        public IEnumerator Choose(List<StoryOption> options, Action<StoryOption> pick)
        {
            Open(); StoryOption chosen = null;
            if (promptText) promptText.text = "";
            ClearChoices();
            // body keeps the top half while choices occupy the bottom half
            RectTransform bodyRt = bodyText ? bodyText.rectTransform : null; Vector2 bodyMin = bodyRt ? bodyRt.anchorMin : Vector2.zero;
            if (bodyRt) bodyRt.anchorMin = new Vector2(bodyMin.x, .5f);
            float avail = choicesParent is RectTransform crt ? crt.rect.height : 100f; float rowH = Mathf.Clamp((avail - 4f * options.Count) / Mathf.Max(1, options.Count), 18f, 30f);
            foreach (var o in options)
            {
                var b = Instantiate(choiceButtonPrefab, choicesParent); b.gameObject.SetActive(true);
                var le = b.GetComponent<LayoutElement>() ?? b.gameObject.AddComponent<LayoutElement>(); le.preferredHeight = rowH; le.minHeight = rowH;
                var rt = b.GetComponent<RectTransform>(); rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 0); rt.sizeDelta = new Vector2(0, rowH);
                var t = b.GetComponentInChildren<Text>(); if (t) { t.text = o.locked ? $"{o.label}  (locked: {o.lockReason})" : o.label; t.horizontalOverflow = HorizontalWrapMode.Overflow; t.fontSize = rowH < 24 ? 14 : 18; }
                b.interactable = !o.locked; if (o.locked && t) t.color = lockedColor;
                var opt = o; b.onClick.AddListener(() => chosen = opt);
            }
            // keyboard: up/down + advance
            // int idx = 0; … Hi();   (previously the first option was pre-highlighted)
            int idx = -1; var buttons = choicesParent.GetComponentsInChildren<Button>();   // nothing highlighted until the player uses the keys
            void Hi() { for (int i = 0; i < buttons.Length; i++) { var cb = buttons[i].colors; cb.normalColor = i == idx ? new Color(1, .9f, .6f) : Color.white; buttons[i].colors = cb; } }
            while (chosen == null)
            {
                if (K != null && K.NavDownDown()) { idx = idx < 0 ? 0 : (idx + 1) % buttons.Length; Hi(); }
                if (K != null && K.NavUpDown()) { idx = idx < 0 ? buttons.Length - 1 : (idx - 1 + buttons.Length) % buttons.Length; Hi(); }
                if (K != null && idx >= 0 && K.AdvanceDown() && buttons.Length > 0 && buttons[idx].interactable) chosen = options[idx];
                yield return null;
            }
            ClearChoices(); if (bodyRt) bodyRt.anchorMin = bodyMin; pick(chosen);
        }

        public void Hide() { if (panel) panel.SetActive(false); ClearChoices(); }

        // ---- internals
        void Open() { if (panel) panel.SetActive(true); }
        void SetPortrait(Sprite s) { if (!portrait) return; portrait.sprite = s; portrait.enabled = s != null; }
        void ClearChoices() { if (!choicesParent) return; for (int i = choicesParent.childCount - 1; i >= 0; i--) { var c = choicesParent.GetChild(i).gameObject; if (c != choiceButtonPrefab?.gameObject) Destroy(c); } }
        IEnumerator Type(string text)
        {
            if (!bodyText) yield break; _skip = false; bodyText.text = ""; if (promptText) promptText.text = "";
            float shown = 0; text = text ?? "";
            while (shown < text.Length)
            {
                if (K != null && K.AdvanceDown()) { _skip = true; }
                shown += (_skip ? 9999 : charsPerSecond * Time.deltaTime);
                bodyText.text = text.Substring(0, Mathf.Min(text.Length, Mathf.FloorToInt(shown)));
                yield return null;
            }
            bodyText.text = text;
            if (promptText) promptText.text = "▼";
        }
        IEnumerator WaitAdvance() { yield return null; while (K == null || !K.AdvanceDown()) yield return null; }
    }

    public class LocationBanner : MonoBehaviour, ILocationBannerUI
    {
        public CanvasGroup group; public Text nameText, subText; public Text descText;   // descText: the location's description, shown under the name
        public Image art; public float hold = 2.2f, fade = .5f;
        [Tooltip("Extra seconds of hold per 10 words of description, so longer text stays readable")] public float holdPerTenWords = 1.0f;
        Coroutine _co; float _extra;
        public void Show(string name, string sub, Sprite sprite) { Show(name, sub, sprite, ""); }
        public void Show(string name, string sub, Sprite sprite, string description)
        {
            if (nameText) nameText.text = name; if (subText) subText.text = sub ?? ""; if (art) { art.sprite = sprite; art.enabled = sprite != null; }
            if (descText) { descText.text = description ?? ""; descText.gameObject.SetActive(!string.IsNullOrEmpty(description)); }
            int words = string.IsNullOrEmpty(description) ? 0 : description.Split(' ').Length;
            _extra = words / 10f * holdPerTenWords;
            if (_co != null) StopCoroutine(_co); _co = StartCoroutine(Run());
        }
        IEnumerator Run()
        {
            if (!group) yield break; group.gameObject.SetActive(true);
            for (float t = 0; t < fade; t += Time.deltaTime) { group.alpha = t / fade; yield return null; }
            group.alpha = 1; yield return new WaitForSeconds(hold + _extra);
            for (float t = 0; t < fade; t += Time.deltaTime) { group.alpha = 1 - t / fade; yield return null; }
            group.alpha = 0; group.gameObject.SetActive(false);
        }
    }

    public class PickupToast : MonoBehaviour, IPickupToastUI
    {
        public CanvasGroup group; public Text text; public Image icon; public float hold = 1.6f;
        Coroutine _co;
        public void Show(string message, Sprite sprite)
        {
            if (text) text.text = message; if (icon) { icon.sprite = sprite; icon.enabled = sprite != null; }
            if (_co != null) StopCoroutine(_co); _co = StartCoroutine(Run());
        }
        IEnumerator Run() { if (!group) yield break; group.gameObject.SetActive(true); group.alpha = 1; yield return new WaitForSeconds(hold); for (float t = 0; t < .4f; t += Time.deltaTime) { group.alpha = 1 - t / .4f; yield return null; } group.gameObject.SetActive(false); }
    }

    public class InventoryHUD : MonoBehaviour, IInventoryUI
    {
        // rows stretch to the list width regardless of how the prefab was authored
        static void FitRow(GameObject row) { var rt = row.GetComponent<RectTransform>(); if (!rt) return; rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(.5f, 1); rt.sizeDelta = new Vector2(0, 28); var le = row.GetComponent<LayoutElement>() ?? row.AddComponent<LayoutElement>(); le.preferredHeight = 28; le.minHeight = 28; var t = row.GetComponentInChildren<Text>(); if (t) { t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow; t.alignment = TextAnchor.UpperLeft; t.supportRichText = true; t.fontSize = 14; } }
        // after a layout pass the text knows its wrapped height → size the row to it
        System.Collections.IEnumerator SizeRows()
        {
            yield return null; if (!listParent) yield break; Canvas.ForceUpdateCanvases();
            foreach (Transform c in listParent) { if (c == null || c.gameObject == rowPrefab) continue; var t = c.GetComponentInChildren<Text>(); var le = c.GetComponent<LayoutElement>(); if (t && le) { float h = t.preferredHeight; if (float.IsNaN(h) || float.IsInfinity(h)) h = 28f; h = Mathf.Clamp(h + 8f, 28f, 160f); le.preferredHeight = h; le.minHeight = h; } }
            var lrt = listParent as RectTransform; if (lrt) LayoutRebuilder.ForceRebuildLayoutImmediate(lrt);
        }
        public GameObject panel; public Transform listParent; public GameObject rowPrefab;   // row: Image + Text
        public void Toggle() { if (!panel) { Debug.LogWarning("Storyloom: InventoryHUD has no panel assigned"); return; } panel.SetActive(!panel.activeSelf); if (panel.activeSelf) { try { Refresh(); } catch (System.Exception e) { Debug.LogError("Storyloom: inventory refresh failed — " + e); } } }
        public bool IsOpen => panel && panel.activeSelf;
        public void Refresh()
        {
            if (!listParent || !StoryloomDirector.Instance) return;
            for (int i = listParent.childCount - 1; i >= 0; i--) { var c = listParent.GetChild(i).gameObject; if (c != rowPrefab) Destroy(c); }
            var d = StoryloomDirector.Instance; int n = 0;
            foreach (var it in d.Runner.Inventory())
            {
                var row = Instantiate(rowPrefab, listParent); row.SetActive(true); n++; FitRow(row);
                var t = row.GetComponentInChildren<Text>(); if (t) { var desc = !string.IsNullOrEmpty(it.description) ? it.description : it.effect; t.text = "<b>" + it.name + "</b>" + (string.IsNullOrEmpty(desc) ? "" : "\n<size=12>" + desc + "</size>"); }
                var b = d.bindings.Item(it.id); var img = row.GetComponentInChildren<Image>(); if (img) { img.sprite = b != null ? b.icon : null; img.enabled = img.sprite != null; }
            }
            if (n == 0) { var row = Instantiate(rowPrefab, listParent); row.SetActive(true); FitRow(row); var t = row.GetComponentInChildren<Text>(); if (t) t.text = "(empty)"; var img = row.GetComponentInChildren<Image>(); if (img) img.enabled = false; }
            if (isActiveAndEnabled && gameObject.activeInHierarchy) StartCoroutine(SizeRows());
        }
    }
}
