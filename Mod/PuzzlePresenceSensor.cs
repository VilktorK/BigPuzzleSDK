using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Mirror;
using UnityEngine;

namespace BigPuzzleSDK {

// What a Presence Sensor detects. Must stay identical, in the same order, to the Unity SDK's enum (Player first: it is what every older puzzle uses).
public enum PuzzlePresenceType { Player, AnyItem, SpecificItem }

// Runtime (mod) version of the SDK's PuzzlePresenceSensor: a button that "presses" when enough players
// stand inside the collider(s) on this object, and "releases" when too few are left. Being a button
// (PuzzleButtonBase), it also works in Press In Order. Everything is decided on the host (it knows where
// every player is); the events then reach every player (see PuzzleEvent in Plugin.cs).
public class PuzzlePresenceSensor : PuzzleButtonBase {
    public PuzzlePresenceSensor(IntPtr ptr) : base(ptr) { }

    // Set in the Inspector (filled in by the loader). The names must match the Unity-side fields.
    // A pack made before the item types existed has none of the new fields, so it keeps the defaults below: it detects players, as before.
    private PuzzlePresenceType type = PuzzlePresenceType.Player;
    private int requiredPlayers = 1;      // players needed inside; in the item types, items needed inside
    private PuzzleHookAccepts acceptedVanilla = PuzzleHookAccepts.None;   // SpecificItem: which Vanilla Item types count
    private List<PuzzleItem> acceptedCustom = new List<PuzzleItem>();     // SpecificItem: which Custom Items count
    private PuzzleEvent onPressed;
    private PuzzleEvent onReleased;
    private PuzzleEvent onPlayerEntered;
    private PuzzleEvent onPlayerLeft;
    private PuzzleEvent onItemEntered;
    private PuzzleEvent onItemLeft;

    private const int PropZoneLayer = 21;   // "PropZone" in the game's layer list: props touch its triggers (like the radio dead zone)
    // Items inside, with how many of this sensor's colliders each one is touching (the item types only; host only)
    private readonly Dictionary<IntPtr, (Prop prop, int touches)> items = new Dictionary<IntPtr, (Prop, int)>();

    private const float CheckSeconds = 0.1f;

    private readonly List<Collider> volumes = new List<Collider>();
    private HashSet<uint> inside = new HashSet<uint>();
    private HashSet<uint> now = new HashSet<uint>();   // swapped with inside each check, so nothing is allocated
    private float nextCheck;
    private bool pressed;
    internal bool IsHeld { get { return pressed; } }   // enough players are inside right now
    private bool set;

    // Read-only for scripts
    public int PlayerCount => inside.Count;

    private void Awake() {
        if (set) return;
        set = true;
        try {
            foreach (Collider c in GetComponents<Collider>()) {
                c.isTrigger = true;   // players walk through it
                volumes.Add(c);
                MeshCollider mesh = c.TryCast<MeshCollider>();
                if (mesh != null && !mesh.convex)
                    Plugin.log.LogWarning($"[PuzzlePresenceSensor] '{name}' has a Mesh Collider that isn't Convex, so it can't tell who is inside. Tick Convex, or use a Box Collider.");
            }
            if (volumes.Count == 0) Plugin.log.LogWarning($"[PuzzlePresenceSensor] '{name}' has no collider, so it never detects anyone. Add a Box (or other) Collider sized to the area.");
            gameObject.layer = LayerHelper.PlayerZoneLayer;   // so it doesn't catch clicks aimed at things inside it
            Renderer r = GetComponent<Renderer>();
            if (r != null) r.enabled = false;   // the placeholder shape is only for the editor
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[PuzzlePresenceSensor] '{name}' could not be set up: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // The item types: the sensor's triggers have to meet props, like the radio dead zone's. (The player type is left exactly as it was.)
    private void Start() {
        if (type == PuzzlePresenceType.Player) return;
        try {
            gameObject.layer = PropZoneLayer;
            Rigidbody body = GetComponent<Rigidbody>();
            if (body == null) body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[PuzzlePresenceSensor] '{name}' could not be set up to detect items: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // The prop a collider belongs to, if any
    private static Prop PropOf(Collider other) {
        if (other == null) return null;
        Rigidbody body = other.attachedRigidbody;
        GameObject root = body != null ? body.gameObject : other.gameObject;
        return root.GetComponent<Prop>();
    }

    // Does this item count? AnyItem: every prop. SpecificItem: a listed Custom Item, or a Vanilla Item type that is ticked
    private bool Wanted(Prop prop) {
        if (type == PuzzlePresenceType.AnyItem) return true;
        foreach (PuzzleItem custom in acceptedCustom) {
            Prop p = custom != null ? custom.GameProp : null;
            if (p != null && p.Pointer == prop.Pointer) return true;
        }
        return (acceptedVanilla & PuzzleHook.KindOf(prop)) != 0;
    }

    private void OnTriggerEnter(Collider other) {
        if (type == PuzzlePresenceType.Player || !PuzzleSDK.IsHost) return;   // the host decides, and tells everyone through the events
        Prop prop = PropOf(other);
        if (prop == null || !Wanted(prop)) return;
        if (items.TryGetValue(prop.Pointer, out var entry)) {
            items[prop.Pointer] = (prop, entry.touches + 1);
            return;
        }
        items[prop.Pointer] = (prop, 1);
        if (onItemEntered != null) onItemEntered.Invoke();
        UpdatePressed(items.Count);   // now, so an item that passes through quickly still presses and releases it
    }

    private void OnTriggerExit(Collider other) {
        if (type == PuzzlePresenceType.Player || !PuzzleSDK.IsHost) return;
        Prop prop = PropOf(other);
        if (prop == null || !items.TryGetValue(prop.Pointer, out var entry)) return;
        if (entry.touches > 1) { items[prop.Pointer] = (prop, entry.touches - 1); return; }
        // Unity can send this when the item's colliders are switched off (a player picked it up). If it is still in here, it stays.
        try { if (!HasEnabledCollider(prop) && PositionInside(prop)) return; } catch (Exception) { }
        items.Remove(prop.Pointer);
        if (onItemLeft != null) onItemLeft.Invoke();
        UpdatePressed(items.Count);
    }

    // An item that is gone (destroyed, switched off, or its colliders switched off, e.g. picked up) is let go: Unity does not always tell us
    private void CheckItems() {
        List<IntPtr> gone = null;
        foreach (var kv in items) {
            if (!StillInside(kv.Value.prop)) (gone ??= new List<IntPtr>()).Add(kv.Key);
        }
        if (gone != null) {
            foreach (IntPtr p in gone) {
                items.Remove(p);
                if (onItemLeft != null) onItemLeft.Invoke();
            }
        }
        UpdatePressed(items.Count);
    }

    // Is a tracked item still inside? An enabled collider (a trigger counts: the game may make a held item's colliders triggers) is
    // compared with the sensor's own; an item with no enabled collider at all (held or picked up with its colliders off: Unity may
    // send an exit for those) is judged by where it is. So an item held inside counts exactly like one lying there.
    private bool StillInside(Prop prop) {
        if (prop == null || !prop.gameObject.activeInHierarchy) return false;
        try {
            bool anyCollider = false;
            foreach (Component c in prop.GetComponentsInChildren(Il2CppType.Of<Collider>(), true)) {
                Collider col = c != null ? c.TryCast<Collider>() : null;
                if (col == null || !col.enabled) continue;
                anyCollider = true;
                foreach (Collider v in volumes)
                    if (v != null && v.enabled && v.bounds.Intersects(col.bounds)) return true;
            }
            if (anyCollider) return false;
            return PositionInside(prop);
        } catch (Exception) { return true; }
    }

    // Where an item is (its position, or the centre of one of its renderers) decides, for an item without colliders
    private bool PositionInside(Prop prop) {
        if (Contains(prop.transform.position)) return true;
        foreach (Component c in prop.GetComponentsInChildren(Il2CppType.Of<Renderer>(), true)) {
            Renderer r = c != null ? c.TryCast<Renderer>() : null;
            if (r != null && r.enabled && Contains(r.bounds.center)) return true;
        }
        return false;
    }

    private static bool HasEnabledCollider(Prop prop) {
        foreach (Component c in prop.GetComponentsInChildren(Il2CppType.Of<Collider>(), true)) {
            Collider col = c != null ? c.TryCast<Collider>() : null;
            if (col != null && col.enabled) return true;
        }
        return false;
    }

    // Is this point inside one of the sensor's colliders?
    private bool Contains(Vector3 point) {
        foreach (Collider v in volumes) {
            if (v == null || !v.enabled) continue;
            if ((v.ClosestPoint(point) - point).sqrMagnitude < 0.0001f) return true;
        }
        return false;
    }

    private void Update() {
        if (!PuzzleSDK.IsHost || volumes.Count == 0 || Time.time < nextCheck) return;
        nextCheck = Time.time + CheckSeconds;
        if (type != PuzzlePresenceType.Player) { CheckItems(); return; }

        // The game keeps its own list of the players, so there is nothing to search for
        now.Clear();
        var characters = PlayerCharacter.allPlayerCharacters;
        for (int i = 0; characters != null && i < characters.Count; i++) {
            PlayerCharacter pc = characters[i];
            if (pc == null) continue;
            PlayerNetworking net = pc.playerNetworking;
            if (net != null && IsInside(pc.transform.position)) now.Add(net.netId);
        }

        foreach (uint id in now) if (!inside.Contains(id) && onPlayerEntered != null) onPlayerEntered.Invoke();
        foreach (uint id in inside) if (!now.Contains(id) && onPlayerLeft != null) onPlayerLeft.Invoke();
        HashSet<uint> swap = inside;
        inside = now;
        now = swap;

        UpdatePressed(inside.Count);
    }

    // Press when enough (players or items) are inside, release when too few are left
    private void UpdatePressed(int count) {
        bool shouldBePressed = count >= Math.Max(1, requiredPlayers);
        if (shouldBePressed == pressed) return;
        pressed = shouldBePressed;
        if (pressed) {
            if (onPressed != null) onPressed.Invoke();
            if (pressedHook != null) pressedHook();
        } else if (onReleased != null) onReleased.Invoke();
    }

    // A player counts when its position, or a point half a metre or a metre above it, is inside the volume
    private bool IsInside(Vector3 position) {
        foreach (Collider c in volumes) {
            if (c == null || !c.enabled) continue;
            for (int i = 0; i < 3; i++) {
                Vector3 p = position + Vector3.up * (i * 0.5f);
                if ((c.ClosestPoint(p) - p).sqrMagnitude < 0.0001f) return true;
            }
        }
        return false;
    }
}

} // namespace BigPuzzleSDK
