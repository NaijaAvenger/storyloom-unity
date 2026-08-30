// Storyloom Unity Kit — event router.
// Event nodes reach game code through one OnStoryEvent(string); every consumer used to write a switch on strings. Drop
// this component anywhere in the scene instead: one row per event name, each with its own UnityEvent to wire cutscenes,
// unlocks, camera shakes or anything else in the inspector. Right-click the component header ▸ "Sync rows from story"
// fills a row for every event name the story fires (existing wiring is kept). Code can also subscribe per event with
// router.On("eventName", handler).
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

namespace Storyloom
{
    public class StoryloomEventRouter : MonoBehaviour
    {
        [Serializable]
        public class Row
        {
            public string eventName;               // matches StoryNode.eventName (see StoryIds.Events after codegen)
            public UnityEvent onFired;
            [Tooltip("Filled at runtime: how many times this event has fired this session")] public int fired;
        }
        public List<Row> rows = new List<Row>();
        [Tooltip("Log a warning when an event fires that has no row here")] public bool warnOnUnrouted = false;

        void OnEnable() { var d = StoryloomDirector.Instance; if (d) d.OnStoryEvent.AddListener(Dispatch); else StartCoroutine(HookWhenReady()); }
        void OnDisable() { var d = StoryloomDirector.Instance; if (d) d.OnStoryEvent.RemoveListener(Dispatch); }
        System.Collections.IEnumerator HookWhenReady()
        {
            while (StoryloomDirector.Instance == null) yield return null;
            StoryloomDirector.Instance.OnStoryEvent.AddListener(Dispatch);
        }

        void Dispatch(string eventName)
        {
            bool routed = false;
            foreach (var r in rows)
                if (r.eventName == eventName) { r.fired++; routed = true; try { r.onFired?.Invoke(); } catch (Exception e) { Debug.LogError($"Storyloom: event '{eventName}' handler threw — {e}", this); } }
            if (_code.TryGetValue(eventName, out var handlers)) { routed = true; foreach (var h in handlers.ToArray()) h?.Invoke(); }
            if (!routed && warnOnUnrouted) Debug.LogWarning($"Storyloom: story event '{eventName}' fired but nothing is wired to it on {name}", this);
        }

        // ---- code-side subscriptions, per event name ----
        readonly Dictionary<string, List<Action>> _code = new Dictionary<string, List<Action>>();
        public void On(string eventName, Action handler) { if (!_code.TryGetValue(eventName, out var l)) _code[eventName] = l = new List<Action>(); l.Add(handler); }
        public void Off(string eventName, Action handler) { if (_code.TryGetValue(eventName, out var l)) l.Remove(handler); }

        [ContextMenu("Sync rows from story")]
        public void SyncRowsFromStory()
        {
            var d = StoryloomDirector.Instance ? StoryloomDirector.Instance : FindObjectOfType<StoryloomDirector>();
            var s = d ? d.Story : null;
            if (s == null) { Debug.LogWarning("Storyloom: no director/story in the scene to sync event rows from", this); return; }
            int added = 0;
            foreach (var n in (s.nodes ?? new StoryNode[0]).Where(n => n.IsEvent && !string.IsNullOrEmpty(n.eventName)).Select(n => n.eventName).Distinct())
                if (!rows.Any(r => r.eventName == n)) { rows.Add(new Row { eventName = n }); added++; }
            Debug.Log($"Storyloom: event router synced — {added} new row(s), {rows.Count} total", this);
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }
    }
}
