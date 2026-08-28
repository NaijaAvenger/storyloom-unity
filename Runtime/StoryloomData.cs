// Storyloom → Unity data model.
// Matches the "Unity JSON" export (format: "storyloom-unity", version 1).
// Plain [Serializable] classes with public fields so Unity's built-in JsonUtility can load it —
// no dictionaries, no properties, no nulls (empty strings / empty arrays instead).
//
// Usage:
//   TextAsset json = Resources.Load<TextAsset>("the-lantern-road.unity");
//   StoryloomStory story = StoryloomStory.FromJson(json.text);

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Storyloom
{
    [Serializable]
    public class StoryloomStory
    {
        public string format;
        public int version;
        public string name;
        public string startNodeId;
        public string[] entryNodeIds;   // export v2.1: every start — the main start plus nodes flagged as entry points (side quests, chapters, hubs)

        public StoryVariable[] variables;
        public BehaviorTag[] behaviorTags;   // export v2.2: named switches decisions can flip; tagged nodes/options need ALL their tags active
        public Character[] characters;
        public Faction[] factions;
        public Species[] species;      // export version 2+
        public Region[] regions;       // export version 2+
        public Location[] locations;
        public Item[] items;
        public Lore[] lore;            // export version 2+
        public StoryNode[] nodes;

        // ---- lookups (built lazily; not serialized) ----
        [NonSerialized] private Dictionary<string, StoryNode> _nodesById;
        [NonSerialized] private Dictionary<string, Character> _charactersById;
        [NonSerialized] private Dictionary<string, Faction> _factionsById;
        [NonSerialized] private Dictionary<string, Location> _locationsById;
        [NonSerialized] private Dictionary<string, Item> _itemsById;
        [NonSerialized] private Dictionary<string, Species> _speciesById;
        [NonSerialized] private Dictionary<string, Region> _regionsById;
        [NonSerialized] private Dictionary<string, Lore> _loreById;

        public static StoryloomStory FromJson(string json)
        {
            var story = JsonUtility.FromJson<StoryloomStory>(json);
            if (story == null) throw new Exception("Storyloom: could not parse JSON.");
            if (story.format != "storyloom-unity")
                Debug.LogWarning($"Storyloom: unexpected format '{story.format}' (expected storyloom-unity). Export with the 'Unity JSON' button.");
            story.BuildIndexes();
            return story;
        }

        public void BuildIndexes()
        {
            _nodesById = Index(nodes, n => n.id);
            _charactersById = Index(characters, c => c.id);
            _factionsById = Index(factions, f => f.id);
            _locationsById = Index(locations, l => l.id);
            _itemsById = Index(items, i => i.id);
            _speciesById = Index(species, s => s.id);
            _regionsById = Index(regions, r => r.id);
            _loreById = Index(lore, l => l.id);
        }

        private static Dictionary<string, T> Index<T>(T[] arr, Func<T, string> key)
        {
            var d = new Dictionary<string, T>();
            if (arr == null) return d;
            foreach (var x in arr) if (x != null && !string.IsNullOrEmpty(key(x))) d[key(x)] = x;
            return d;
        }

        public StoryNode GetNode(string id) { if (_nodesById == null) BuildIndexes(); return id != null && _nodesById.TryGetValue(id, out var n) ? n : null; }
        public Character GetCharacter(string id) { if (_charactersById == null) BuildIndexes(); return id != null && _charactersById.TryGetValue(id, out var c) ? c : null; }
        public Faction GetFaction(string id) { if (_factionsById == null) BuildIndexes(); return id != null && _factionsById.TryGetValue(id, out var f) ? f : null; }
        public Location GetLocation(string id) { if (_locationsById == null) BuildIndexes(); return id != null && _locationsById.TryGetValue(id, out var l) ? l : null; }
        public Item GetItem(string id) { if (_itemsById == null) BuildIndexes(); return id != null && _itemsById.TryGetValue(id, out var i) ? i : null; }
        public Species GetSpecies(string id) { if (_speciesById == null) BuildIndexes(); return id != null && _speciesById.TryGetValue(id, out var s) ? s : null; }
        public Region GetRegion(string id) { if (_regionsById == null) BuildIndexes(); return id != null && _regionsById.TryGetValue(id, out var r) ? r : null; }
        public Lore GetLore(string id) { if (_loreById == null) BuildIndexes(); return id != null && _loreById.TryGetValue(id, out var l) ? l : null; }
        public BehaviorTag GetBehaviorTag(string id) { if (behaviorTags == null) return null; foreach (var t in behaviorTags) if (t != null && t.id == id) return t; return null; }
        public StoryNode StartNode => GetNode(startNodeId);

        /// <summary>Optional side content placed at a main-thread node (Discoverable nodes with hostNodeId == nodeId).</summary>
        public IEnumerable<StoryNode> DiscoverablesAt(string nodeId)
        {
            if (nodes == null) yield break;
            foreach (var n in nodes) if (n != null && n.IsDiscoverable && n.hostNodeId == nodeId) yield return n;
        }
        /// <summary>Region chain for a location, innermost first (village → country → world).</summary>
        public IEnumerable<Region> RegionsOf(Location loc)
        {
            var r = loc == null ? null : GetRegion(loc.regionId);
            var guard = 0;
            while (r != null && guard++ < 64) { yield return r; r = GetRegion(r.parentRegionId); }
        }
        /// <summary>All characters with the given role type ("protagonist", "antagonist", "npc", …).</summary>
        public IEnumerable<Character> CharactersByRole(string roleType)
        {
            if (characters == null) yield break;
            foreach (var c in characters) if (string.Equals(c.roleType, roleType, StringComparison.OrdinalIgnoreCase)) yield return c;
        }
        /// <summary>All characters belonging to a faction (or any of its sub-factions when includeChildren is true).</summary>
        public IEnumerable<Character> CharactersInFaction(string factionId, bool includeChildren = false)
        {
            if (characters == null) yield break;
            foreach (var c in characters)
            {
                var f = GetFaction(c.factionId); var guard = 0;
                while (f != null && guard++ < 64) { if (f.id == factionId) { yield return c; break; } if (!includeChildren) break; f = GetFaction(f.parentFactionId); }
            }
        }
    }

    /// <summary>Variable declaration. type is "bool", "int", "float" or "string" ("number" in exports older than version 2). defaultValue is always a string in the JSON.</summary>
    [Serializable]
    public class StoryVariable
    {
        public string name;
        public string type;
        public string defaultValue;
        public bool tracked;      // flagged for technical review in the editor (informational)
    }

    [Serializable]
    public class VoiceClip
    {
        public string name;
        public string mime;   // e.g. audio/mpeg
        public string data;   // data URI; decode with StoryloomAudio.ToBytes
    }

    /* --- previous data classes (export version 1; kept for reference) ---
    public class Character { public string id, name, description, image, role, traits, motivation; public string voice; public VoiceClip[] voiceSamples; }
    public class Faction   { public string id, name, description, image, goals; }
    public class Location  { public string id, name, description, image, region; }
    public class Item      { public string id, name, description, image, effect; public bool startOwned; }
    */

    /// <summary>A behavior tag (export v2.2): a named switch decisions can flip with tag effects. A node or choice option
    /// carrying behaviorTagIds is only available while every one of its tags is active; startsOn is the state at story start.</summary>
    [Serializable]
    public class BehaviorTag
    {
        public string id;
        public string name;
        public bool startsOn = true;
    }

    // An idle / revisit line a character can say when they have no main beat to play (or none yet). Picked by:
    // 1) lines whose conditions pass (conditions always outrank unconditional lines), by priority;
    // 2) lines pinned to this exact visit number (1st revisit, 2nd revisit…), by priority;
    // 3) any-visit lines by priority, cycling through ties so repeat visits vary.
    [Serializable]
    public class Bark
    {
        public string id;
        public string text;
        public string emotion;
        public int visit;              // 0 = any visit; N = only on the Nth revisit
        public int priority;           // higher wins within its group
        public bool once;              // never repeated after it has been said
        public string conditionMode;   // all | any
        public Condition[] conditions;
    }

    [Serializable]
    public class Relationship
    {
        public string characterId;   // the other character
        public string kind;          // family, parent, child, sibling, spouse, lover, friend, ally, rival, enemy, mentor, student, employer, servant, acquaintance, other
        public string note;
    }

    [Serializable]
    public class Character
    {
        public string id, name, description, image, role, traits, motivation;
        public string roleType;            // protagonist, antagonist, deuteragonist, ally, mentor, rival, love interest, npc, minor, player (or custom text)
        public string speciesId;           // -> StoryloomStory.GetSpecies
        public string factionId;           // -> StoryloomStory.GetFaction
        public string homeLocationId;      // -> StoryloomStory.GetLocation
        public Relationship[] relationships;
        public string voice;               // voice direction note (tone, accent, pace)
        public VoiceClip[] voiceSamples;   // reference clips for casting / VO

        public bool IsProtagonist => string.Equals(roleType, "protagonist", StringComparison.OrdinalIgnoreCase) || string.Equals(roleType, "player", StringComparison.OrdinalIgnoreCase);
        public bool IsAntagonist  => string.Equals(roleType, "antagonist", StringComparison.OrdinalIgnoreCase);
        public bool IsNpc         => string.Equals(roleType, "npc", StringComparison.OrdinalIgnoreCase) || string.Equals(roleType, "minor", StringComparison.OrdinalIgnoreCase);
    }

    [Serializable]
    public class Faction
    {
        public string id, name, description, image, goals;
        public string kind;              // faction, clan, guild, family, tribe, government, religion, military, gang, corporation, order, company, crew (or custom)
        public string baseLocationId;    // -> GetLocation
        public string parentFactionId;   // -> GetFaction (a clan inside a kingdom, a crew inside a gang…)
    }

    [Serializable]
    public class Species
    {
        public string id, name, description, image, traits;
        public string kind;              // species, race, creature, monster, animal, spirit, machine, deity
    }

    [Serializable]
    public class Region
    {
        public string id, name, description, image;
        public string kind;              // world, continent, country, kingdom, empire, province, state, region, county, city, town, village, district, island, realm
        public string parentRegionId;    // -> GetRegion; empty at the top of the tree
    }

    [Serializable]
    public class Location
    {
        public string id, name, description, image;
        public string kind;              // place, building, room, street, landmark, wilderness, dungeon, shop, tavern, temple, castle, camp, vehicle, ship
        public string regionId;          // -> GetRegion
        public string region;            // region name (kept for older code)
        public string atmosphere;
    }

    [Serializable]
    public class Item
    {
        public string id, name, description, image, effect;
        public string kind;       // item, key item, weapon, armor, consumable, document, currency, tool, quest item, clue, relic
        public bool startOwned;   // player begins the story holding it
        public bool tracked;      // flagged for technical review in the editor (informational)
    }

    [Serializable]
    public class Lore
    {
        public string id, name, description, image;
        public string kind;       // history, rule, myth, religion, culture, technology, magic, language, custom, timeline, organisation
    }

    /// <summary>
    /// One rule: variable [op] value. op is one of == != > < >= <= "is set" "not set".
    /// Inventory rules use variable "item:&lt;itemId&gt;" with op "has" or "lacks" (value unused).
    /// </summary>
    [Serializable]
    public class Condition
    {
        public string variable;
        public string op;
        public string value;

        public override string ToString() =>
            op == "is set" || op == "not set" ? $"{variable} {op}" : $"{variable} {op} {value}";
    }

    /// <summary>Applied when a node is entered. op is one of set add subtract toggle; inventory effects use variable "item:&lt;itemId&gt;" with op give or take.</summary>
    [Serializable]
    public class Effect
    {
        public string variable;
        public string op;
        public string value;
    }

    /// <summary>A Choice node option. Its id doubles as the output port name used by links.</summary>
    [Serializable]
    public class ChoiceOption
    {
        public string id;
        public string label;
        public int weight = 1;         // Random nodes: relative probability (0 = never)
        public string conditionMode;   // "all" | "any"
        public Condition[] conditions;
        public string[] behaviorTagIds;   // export v2.2: option shown only while all these tags are active
    }

    /// <summary>An outgoing link. port is "out" for most nodes, "pass"/"fail" for Check nodes, or an option id for Choice nodes.</summary>
    [Serializable]
    public class Link
    {
        public string port;
        public string toNodeId;
        public string label;
        public string conditionMode;
        public Condition[] conditions;
    }

    /// <summary>One spoken line inside a Dialogue node.</summary>
    [Serializable]
    public class Line
    {
        public string speakerId;
        public string text;
        public string emotion;
    }

    /// <summary>type is one of: scene dialogue choice check unlock ending jump random event.</summary>
    [Serializable]
    public class StoryNode
    {
        public string id;
        public string type;
        public string title;
        public string text;            // for Dialogue nodes this is all lines joined; prefer `lines`

        public string speakerId;       // first speaker for Dialogue nodes
        public string locationId;
        public string when;
        public string why;
        public string jumpToNodeId;    // Jump nodes only: the node to continue at
        public string eventName;       // Event nodes only: the trigger name your game listens for
        public string hostNodeId;      // Discoverable nodes only: the main-thread node where this optional content can be found
        public bool entry;             // an extra start point: the player can begin the story here too (always available under strict order)
        public string discoverKind;    // Discoverable nodes only: secret, side quest, collectible, lore, encounter, puzzle, vendor, shortcut
        public Line[] lines;           // Dialogue nodes only; empty otherwise

        public string[] characterIds;
        public string[] factionIds;
        public string[] itemIds;
        public string[] speciesIds;    // export version 2+
        public string[] loreIds;       // lore entries revealed at this node (export version 2+)
        public string image;   // data URI ("data:image/jpeg;base64,...") or empty

        public string conditionMode;     // entry requirements (or the test, for Check nodes)
        public Condition[] conditions;
        public Effect[] effects;
        public ChoiceOption[] options;   // Choice nodes only; empty otherwise
        public Link[] links;
        public string[] behaviorTagIds;  // export v2.2: node available only while all these tags are active

        public bool IsChoice => type == "choice";
        public bool IsCheck => type == "check";
        public bool IsEnding => type == "ending";
        public bool IsJump => type == "jump";
        public bool IsDialogue => type == "dialogue";
        public bool IsRandom => type == "random";
        public bool IsEvent => type == "event";
        public bool IsDiscoverable => type == "discoverable";

        public IEnumerable<Link> LinksFrom(string port)
        {
            if (links == null) yield break;
            foreach (var l in links) if (l.port == port) yield return l;
        }
    }

    public static class StoryloomAudio
    {
        /// <summary>Raw bytes of a voice clip (data URI). Feed to your own decoder / AudioClip loader.</summary>
        public static byte[] ToBytes(string dataUri)
        {
            if (string.IsNullOrEmpty(dataUri)) return null;
            int comma = dataUri.IndexOf(',');
            if (comma < 0) return null;
            try { return Convert.FromBase64String(dataUri.Substring(comma + 1)); } catch { return null; }
        }
    }

    public static class StoryloomImages
    {
        /// <summary>Decode a node/entity image (data URI) into a Texture2D. Returns null when empty or unparseable.</summary>
        public static Texture2D ToTexture(string dataUri)
        {
            if (string.IsNullOrEmpty(dataUri)) return null;
            int comma = dataUri.IndexOf(',');
            if (comma < 0) return null;
            try
            {
                byte[] bytes = Convert.FromBase64String(dataUri.Substring(comma + 1));
                var tex = new Texture2D(2, 2);
                return tex.LoadImage(bytes) ? tex : null;
            }
            catch { return null; }
        }
    }
}
