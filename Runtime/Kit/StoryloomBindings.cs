// Storyloom Unity Kit — bindings.
// Maps every Storyloom entity id (characters, items, locations, discoverables) to the Unity things that represent it:
// a prefab to place in the world, a portrait / icon sprite, and per-kind extras. The editor window fills the id/name
// columns from the story asset and keeps your assignments when you re-import a newer export.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Storyloom
{
    [Serializable]
    public class CharacterBinding
    {
        public string id, name, roleType;
        public GameObject prefab;      // walking/idle NPC prefab (needs an NpcInteractable; the kit adds one if missing)
        public Sprite portrait;        // dialogue box portrait (falls back to the Storyloom image if empty)
        public Sprite worldSprite;     // sprite used by the placeholder prefab
        public AudioClip voiceBark;    // optional: plays when their dialogue line starts
    }
    [Serializable]
    public class ItemBinding
    {
        public string id, name, kind;
        public GameObject prefab;      // pickup prefab (ItemPickup); placeholder if empty
        public Sprite icon;            // inventory / toast icon
        public bool stackable;
    }
    [Serializable]
    public class LocationBinding
    {
        public string id, name, kind;
        public string sceneName;       // Unity scene to load when the story moves here (blank = stay in the current scene)
        public GameObject prefab;      // optional: a trigger volume / signpost prefab (LocationTrigger)
        public Sprite banner;          // optional: art shown with the location banner
        public AudioClip ambience;
    }
    [Serializable]
    public class DiscoverableBinding
    {
        public string nodeId, title, kind, hostNodeId;
        public GameObject prefab;      // world object the player finds (DiscoverableInteractable); placeholder if empty
        public Sprite worldSprite;
    }

    [Serializable]
    public class StartingValue { public string name; public string value; public bool enabled = true; }   // name = variable name or "item:<id>" (value "true"/"false")

    [CreateAssetMenu(menuName = "Storyloom/Bindings", fileName = "StoryloomBindings")]
    public class StoryloomBindings : ScriptableObject
    {
        public StoryloomStoryAsset story;
        [Tooltip("Which kind of test scene 'Create scene' builds: top-down (Stardew-style), third person or first person. Interactables and UI are shared.")]
        public GameStyle gameStyle = GameStyle.TopDown;
        [Tooltip("Overrides for the story's starting variables and starting inventory; applied whenever the runner resets")]
        public List<StartingValue> startingValues = new List<StartingValue>();
        public StartingValue Starting(string name) => startingValues.Find(v => v.name == name);
        /// <summary>Apply the enabled overrides to a fresh runner.</summary>
        public void ApplyStartingValues(StoryRunner r)
        {
            if (r == null) return;
            foreach (var v in startingValues)
            {
                if (!v.enabled || string.IsNullOrEmpty(v.name)) continue;
                if (v.name.StartsWith(StoryRunner.ItemPrefix)) { var id = v.name.Substring(StoryRunner.ItemPrefix.Length); if (string.Equals(v.value, "true", StringComparison.OrdinalIgnoreCase)) r.GiveItem(id); else r.TakeItem(id); }
                else r.Set(v.name, v.value);
            }
        }
        public List<CharacterBinding> characters = new List<CharacterBinding>();
        public List<ItemBinding> items = new List<ItemBinding>();
        public List<LocationBinding> locations = new List<LocationBinding>();
        public List<DiscoverableBinding> discoverables = new List<DiscoverableBinding>();

        [Header("Defaults (used when a binding has no prefab)")]
        public GameObject defaultNpcPrefab;
        public GameObject defaultItemPrefab;
        public GameObject defaultDiscoverablePrefab;

        public CharacterBinding Character(string id) => characters.Find(b => b.id == id);
        public ItemBinding Item(string id) => items.Find(b => b.id == id);
        public LocationBinding Location(string id) => locations.Find(b => b.id == id);
        public DiscoverableBinding Discoverable(string nodeId) => discoverables.Find(b => b.nodeId == nodeId);

        /// <summary>Add rows for anything new in the story and refresh names; never drops or overwrites your assignments.</summary>
        public int SyncFromStory()
        {
            var s = story != null ? story.Story : null; if (s == null) return 0;
            int added = 0;
            foreach (var c in s.characters ?? new Character[0]) { var b = Character(c.id); if (b == null) { b = new CharacterBinding { id = c.id }; characters.Add(b); added++; } b.name = c.name; b.roleType = c.roleType; }
            foreach (var i in s.items ?? new Item[0]) { var b = Item(i.id); if (b == null) { b = new ItemBinding { id = i.id }; items.Add(b); added++; } b.name = i.name; b.kind = i.kind; }
            foreach (var l in s.locations ?? new Location[0]) { var b = Location(l.id); if (b == null) { b = new LocationBinding { id = l.id }; locations.Add(b); added++; } b.name = l.name; b.kind = l.kind; }
            foreach (var n in s.nodes ?? new StoryNode[0]) { if (!n.IsDiscoverable) continue; var b = Discoverable(n.id); if (b == null) { b = new DiscoverableBinding { nodeId = n.id }; discoverables.Add(b); added++; } b.title = n.title; b.kind = n.discoverKind; b.hostNodeId = n.hostNodeId; }
            return added;
        }

        /// <summary>Human-readable list of what still needs a prefab / sprite. Empty = ready to play.</summary>
        public List<string> Unbound()
        {
            var out_ = new List<string>();
            foreach (var b in characters) if (b.prefab == null && defaultNpcPrefab == null) out_.Add($"Character '{b.name}' has no prefab (and no default NPC prefab)");
            foreach (var b in items) if (b.prefab == null && defaultItemPrefab == null) out_.Add($"Item '{b.name}' has no prefab (and no default item prefab)");
            foreach (var b in discoverables) if (b.prefab == null && defaultDiscoverablePrefab == null) out_.Add($"Discoverable '{b.title}' has no prefab");
            return out_;
        }
    }
}
