// Storyloom Unity Kit — location anchors: map story locations onto your real levels.
// The generated test scene lays locations out in an artificial lane; an anchor goes the other way. Drop a LocationAnchor
// into your own scene, assign the location's entity asset, and "Populate from story" (in its inspector) places that
// location's NPCs, items, discoverables and signpost under it — at your hand-placed StoryloomSpawnPoints first, then in
// a grid around the anchor — plus the zone trigger, all wired through the entity assets. Populate is idempotent (spawned
// objects are marked and skipped next time) and "Clear generated" removes only what it created, never your own objects.
using UnityEngine;

namespace Storyloom
{
    public class LocationAnchor : MonoBehaviour
    {
        [Tooltip("The location this anchor stands for (Window ▸ Storyloom ▸ Generate entity assets)")]
        public StoryloomLocationAsset location;
        [Header("What Populate places")]
        public bool addZoneTrigger = true;
        [Tooltip("Zone volume size. 3D styles use x/y/z; top-down uses x (width) and z (height).")]
        public Vector3 zoneSize = new Vector3(11, 4, 12);
        public bool placeSignpost = true;
        public bool placeNpcs = true, placeItems = true, placeDiscoverables = true;
        [Tooltip("Grid spacing for entities that have no StoryloomSpawnPoint to land on")]
        public float spacing = 2f;

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(.3f, .8f, 1f, .5f);
            var p = StoryloomPlayer.Current; bool xz = p ? p.UsesXZ : true;
            Gizmos.DrawWireCube(transform.position + (xz ? new Vector3(0, zoneSize.y * .5f, 0) : Vector3.zero),
                                xz ? zoneSize : new Vector3(zoneSize.x, zoneSize.z, 0.1f));
            foreach (var sp in GetComponentsInChildren<StoryloomSpawnPoint>())
            { Gizmos.color = new Color(1, .8f, .2f, .8f); Gizmos.DrawWireSphere(sp.transform.position, .3f); }
        }
    }

    /// <summary>A hand-placed spot an anchor's Populate uses before falling back to the grid. Set preferredEntityId to
    /// reserve it for one specific character / item / discoverable.</summary>
    public class StoryloomSpawnPoint : MonoBehaviour
    {
        [Tooltip("Optional: the Storyloom id this spot is reserved for (empty = first come, first served)")]
        public string preferredEntityId;
    }

    /// <summary>Marks an object an anchor's Populate created, so re-populating skips it and Clear removes only these.</summary>
    public class StoryloomAnchorSpawn : MonoBehaviour
    {
        public string key;                       // "c:<id>", "i:<id>", "d:<id>", "zone", "sign"
        public StoryloomSpawnPoint usedPoint;    // the spawn point it consumed, if any
    }
}
