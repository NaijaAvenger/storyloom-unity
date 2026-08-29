// Storyloom Unity Kit — story asset.
// A ScriptableObject that wraps an exported .unity.json (kept as text so it survives version control diffs)
// and parses it on demand. Created by Storyloom ▸ Import story JSON… (see Editor/StoryloomEditorWindow.cs).
using System;
using UnityEngine;

namespace Storyloom
{
    [CreateAssetMenu(menuName = "Storyloom/Story Asset", fileName = "StoryloomStory")]
    public class StoryloomStoryAsset : ScriptableObject
    {
        [TextArea(3, 8)] public string sourceInfo;   // where it came from, export date — informational
        public TextAsset json;                        // the exported *.unity.json
        [HideInInspector] public string jsonText;     // fallback copy when no TextAsset is assigned
        [Tooltip("Live link: the URL that serves this workbook's Unity JSON export (same payload as File ▸ Export Unity JSON on storyloom.com). When set, 'Re-sync from story' pulls the latest export from here — no manual download/import round-trip. Auth token, if the workbook needs one, is stored per-machine in the editor, never in this asset.")]
        public string liveUrl;                        // empty = no live link; re-sync stays local

        [NonSerialized] private StoryloomStory _story;

        public StoryloomStory Story
        {
            get
            {
                if (_story == null)
                {
                    var text = json != null ? json.text : jsonText;
                    if (string.IsNullOrEmpty(text)) { Debug.LogError($"Storyloom: '{name}' has no JSON."); return null; }
                    _story = StoryloomStory.FromJson(text);
                }
                return _story;
            }
        }
        public void Invalidate() { _story = null; }
    }
}
