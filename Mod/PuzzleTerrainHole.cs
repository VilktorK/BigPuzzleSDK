using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace BigPuzzleSDK {

// Runtime (mod) version of the SDK's PuzzleTerrainHole: the collider(s) on this object cut a hole
// in the world's Terrain wherever they touch its surface, and remove the trees (and rocks) they
// overlap, so a puzzle can sit underground. Everything is put back when the puzzle unloads.
//
// It is static scenery from the pack, so every PC does the same thing on its own; nothing is sent
// over the network.
//
// Terrain holes: the game's build has no SetHoles / IsHole left in it, but it does keep the call the
// terrain painting tools use, which copies the active RenderTexture into the terrain's hole map. So
// each run of hole cells is painted from a tiny RenderTexture (0 = hole, 1 = solid). A second later
// a ray through the hole checks the terrain really stopped being solid there; if not, the opposite
// value is tried, and if that fails too the terrain is put back and a warning is logged.
public class PuzzleTerrainHole : MonoBehaviour {
    public PuzzleTerrainHole(IntPtr ptr) : base(ptr) { }

    // Set in the Inspector; the names must match the Unity-side fields
    internal bool cutHoles = true;
    internal bool removeTrees = true;
    internal bool removeRocks = true;
    internal bool removeShrubs = true;

    private class Cut {
        public Terrain terrain; public TerrainData data;
        public int x0, z0, w, h; public bool[] mask;     // the block of cells and which of them are cut
        public float holeValue;                          // what is painted to make a hole (0, or 1 if the map is the other way round)
        public Vector3 probe;                            // a point on the surface inside the hole, to check it with a ray
        public bool flipped, failed;
    }
    private readonly List<Cut> cuts = new List<Cut>();
    private readonly List<(TerrainData data, TreeInstance tree)> removedTrees = new List<(TerrainData, TreeInstance)>();
    private class DetailCut { public TerrainData data; public int layer, x0, y0, w, h; public int[] original; }
    private readonly List<DetailCut> detailCuts = new List<DetailCut>();
    private readonly List<GameObject> hiddenRocks = new List<GameObject>();

    private Collider[] colliders;
    private bool applied;
    private Cut verifying;
    private int verifyFrame;
    private float nextTry;
    private int tries;
    private const int MaxTries = 12;      // the terrain may load a moment after the puzzle: try for ~12 s

    private void Awake() {
        try {
            colliders = GetComponents<Collider>();
            if (colliders.Length == 0) {
                Plugin.log.LogWarning($"[PuzzleTerrainHole] '{name}' has no collider, so it covers nothing. Add a Box (or other) Collider sized to the hole.");
                applied = true;
                return;
            }
            foreach (Collider c in colliders) c.isTrigger = true;
            Renderer r = GetComponent<Renderer>();
            if (r != null) r.enabled = false;   // the placeholder shape is only for the editor
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[PuzzleTerrainHole] '{name}' could not be set up: {ex.GetType().Name}: {ex.Message}");
            applied = true;
        }
    }

    private void Update() {
        if (verifying != null && Time.frameCount >= verifyFrame) Verify();
        if (applied || Time.unscaledTime < nextTry) return;
        nextTry = Time.unscaledTime + 1f;
        tries++;
        try {
            Terrain[] terrains = Terrain.activeTerrains;
            if (terrains == null || terrains.Length == 0) {
                if (tries >= MaxTries) { applied = true; Plugin.log.LogWarning($"[PuzzleTerrainHole] '{name}': no terrain found, nothing to cut."); }
                return;
            }
            applied = true;
            Physics.SyncTransforms();
            Bounds area = Union();
            int holes = 0, trees = 0, rocks = 0, shrubs = 0;
            foreach (Terrain t in terrains) {
                if (t == null || t.terrainData == null) continue;
                if (cutHoles) holes += CutHoles(t, area);
                if (removeTrees) trees += RemoveTrees(t, area);
                if (removeShrubs) shrubs += RemoveShrubs(t, area);
            }
            if (removeRocks) rocks = RemoveRocks(area);
        } catch (Exception ex) {
            applied = true;
            Plugin.log.LogWarning($"[PuzzleTerrainHole] '{name}' failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private Bounds Union() {
        Bounds b = colliders[0].bounds;
        for (int i = 1; i < colliders.Length; i++) b.Encapsulate(colliders[i].bounds);
        return b;
    }

    // True when the point is inside (or within 'margin' of) any collider here
    private bool Touches(Vector3 p, float margin) {
        foreach (Collider c in colliders) {
            if (c == null || !c.enabled) continue;
            Vector3 q = c.ClosestPoint(p);
            if ((q - p).sqrMagnitude <= margin * margin) return true;
        }
        return false;
    }

    // ---- holes ----

    private int CutHoles(Terrain terrain, Bounds area) {
        TerrainData data = terrain.terrainData;
        int res = data.holesResolution;
        Vector3 size = data.size;
        Vector3 origin = terrain.transform.position;
        if (res <= 0 || size.x <= 0f || size.z <= 0f) {
            Plugin.log.LogWarning($"[PuzzleTerrainHole] '{name}': terrain '{terrain.name}' has holesResolution={res}, size={size}; skipped.");
            return 0;
        }
        // a box wholly outside this terrain: skip it
        if (area.max.x < origin.x || area.min.x > origin.x + size.x || area.max.z < origin.z || area.min.z > origin.z + size.z) {
            return 0;
        }

        float cellX = size.x / res, cellZ = size.z / res;
        int x0 = Mathf.Clamp(Mathf.FloorToInt((area.min.x - origin.x) / cellX) - 1, 0, res - 1);
        int x1 = Mathf.Clamp(Mathf.CeilToInt((area.max.x - origin.x) / cellX) + 1, 0, res - 1);
        int z0 = Mathf.Clamp(Mathf.FloorToInt((area.min.z - origin.z) / cellZ) - 1, 0, res - 1);
        int z1 = Mathf.Clamp(Mathf.CeilToInt((area.max.z - origin.z) / cellZ) + 1, 0, res - 1);
        if (x1 < x0 || z1 < z0) return 0;

        int w = x1 - x0 + 1, h = z1 - z0 + 1;
        bool[] mask = new bool[w * h];
        int count = 0;
        Vector3 probe = Vector3.zero;
        for (int z = 0; z < h; z++) {
            for (int x = 0; x < w; x++) {
                // the surface at the cell's centre and corners: if any touches the collider, the cell is cut
                bool hit = false;
                float cx = 0f, cy = 0f, cz = 0f;
                for (int k = 0; k < 5 && !hit; k++) {
                    cx = (x0 + x + (k == 4 ? 0.5f : (k & 1))) * cellX + origin.x;
                    cz = (z0 + z + (k == 4 ? 0.5f : ((k >> 1) & 1))) * cellZ + origin.z;
                    cy = terrain.SampleHeight(new Vector3(cx, 0f, cz)) + origin.y;
                    hit = Touches(new Vector3(cx, cy, cz), 0.02f);
                }
                if (!hit) continue;
                mask[z * w + x] = true;
                count++;
                if (probe == Vector3.zero) probe = new Vector3((x0 + x + 0.5f) * cellX + origin.x, terrain.SampleHeight(new Vector3((x0 + x + 0.5f) * cellX + origin.x, 0f, (z0 + z + 0.5f) * cellZ + origin.z)) + origin.y, (z0 + z + 0.5f) * cellZ + origin.z);
            }
        }
        if (count == 0) return 0;

        Cut cut = new Cut { terrain = terrain, data = data, x0 = x0, z0 = z0, w = w, h = h, mask = mask, holeValue = 0f, probe = probe };
        if (!Paint(cut, cut.holeValue)) {
            Plugin.log.LogWarning($"[PuzzleTerrainHole] '{name}': could not paint the terrain's hole map, so no hole was cut.");
            return 0;
        }
        cuts.Add(cut);
        verifying = cut;
        verifyFrame = Time.frameCount + 90;     // give the terrain's collider time to rebuild
        return count;
    }

    // A second after painting: is the terrain still solid in the middle of the hole?
    private void Verify() {
        Cut cut = verifying;
        verifying = null;
        try {
            if (cut.terrain == null || SolidAt(cut.probe)) {
                if (!cut.flipped) {
                    // maybe the hole map is the other way round: try the opposite value
                    cut.flipped = true;
                    cut.holeValue = 1f;
                    Paint(cut, cut.holeValue);
                    verifying = cut;
                    verifyFrame = Time.frameCount + 90;
                    return;
                }
                cut.failed = true;
                Paint(cut, 1f - cut.holeValue);
                cuts.Remove(cut);
                Plugin.log.LogWarning($"[PuzzleTerrainHole] '{name}': the terrain stayed solid after painting the holes both ways, so it was put back.");
                return;
            }
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[PuzzleTerrainHole] '{name}': could not check the hole: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // True if a ray straight down through the point meets the terrain's collider
    private static bool SolidAt(Vector3 p) {
        var hits = Physics.RaycastAll(p + Vector3.up * 3f, Vector3.down, 6f);
        if (hits == null) return false;
        foreach (RaycastHit hit in hits)
            if (hit.collider != null && hit.collider.TryCast<TerrainCollider>() != null) return true;
        return false;
    }

    // Paints 'value' over the cut cells of the terrain's hole map, one run per row, from a one-row RenderTexture
    private static bool Paint(Cut cut, float value) {
        RenderTexture previous = RenderTexture.active;
        RenderTexture rt = null;
        try {
            rt = new RenderTexture(cut.w, 1, 0, RenderTextureFormat.R8);
            rt.Create();
            RenderTexture.active = rt;
            GL.Clear(false, true, new Color(value, value, value, value));
            for (int z = 0; z < cut.h; z++) {
                int x = 0;
                while (x < cut.w) {
                    if (!cut.mask[z * cut.w + x]) { x++; continue; }
                    int start = x;
                    while (x < cut.w && cut.mask[z * cut.w + x]) x++;
                    cut.data.Internal_CopyActiveRenderTextureToHoles(new RectInt(0, 0, x - start, 1), cut.x0 + start, cut.z0 + z, false);
                }
            }
            return true;
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[PuzzleTerrainHole] painting holes failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        } finally {
            RenderTexture.active = previous;
            if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); }
        }
    }

    // ---- shrubs and grass (the terrain's "details") ----

    private static IntPtr setDetail, int2DArray;
    private static bool setDetailLooked;

    private int RemoveShrubs(Terrain terrain, Bounds area) {
        TerrainData data = terrain.terrainData;
        int dw = data.detailWidth, dh = data.detailHeight;
        var protos = data.detailPrototypes;
        if (dw <= 0 || dh <= 0 || protos == null || protos.Length == 0) return 0;
        Vector3 size = data.size, origin = terrain.transform.position;
        if (area.max.x < origin.x || area.min.x > origin.x + size.x || area.max.z < origin.z || area.min.z > origin.z + size.z) return 0;

        float cellX = size.x / dw, cellZ = size.z / dh;
        int x0 = Mathf.Clamp(Mathf.FloorToInt((area.min.x - origin.x) / cellX) - 1, 0, dw - 1);
        int x1 = Mathf.Clamp(Mathf.CeilToInt((area.max.x - origin.x) / cellX) + 1, 0, dw - 1);
        int y0 = Mathf.Clamp(Mathf.FloorToInt((area.min.z - origin.z) / cellZ) - 1, 0, dh - 1);
        int y1 = Mathf.Clamp(Mathf.CeilToInt((area.max.z - origin.z) / cellZ) + 1, 0, dh - 1);
        int w = x1 - x0 + 1, h = y1 - y0 + 1;
        if (w <= 0 || h <= 0) return 0;

        bool[] mask = new bool[w * h];
        int count = 0;
        float margin = Mathf.Max(cellX, cellZ) * 0.5f;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) {
                float cx = (x0 + x + 0.5f) * cellX + origin.x, cz = (y0 + y + 0.5f) * cellZ + origin.z;
                float cy = terrain.SampleHeight(new Vector3(cx, 0f, cz)) + origin.y;
                if (Touches(new Vector3(cx, cy, cz), margin)) { mask[y * w + x] = true; count++; }
            }
        if (count == 0) return 0;

        int changed = 0;
        for (int layer = 0; layer < protos.Length; layer++) {
            int[] original = ReadDetails(data, x0, y0, w, h, layer);
            if (original == null) continue;
            int[] cleared = (int[])original.Clone();
            bool any = false;
            for (int i = 0; i < cleared.Length; i++) if (mask[i] && cleared[i] != 0) { cleared[i] = 0; any = true; }
            if (!any) continue;
            if (!WriteDetails(data, x0, y0, w, h, layer, cleared)) {
                Plugin.log.LogWarning($"[PuzzleTerrainHole] '{name}': could not clear shrubs/grass on the terrain (the game has no way to set them).");
                return changed;
            }
            detailCuts.Add(new DetailCut { data = data, layer = layer, x0 = x0, y0 = y0, w = w, h = h, original = original });
            for (int i = 0; i < cleared.Length; i++) if (mask[i] && original[i] != 0) changed++;
        }
        return changed;
    }

    // The block of one detail layer as a flat [y * w + x] array of counts
    private static unsafe int[] ReadDetails(TerrainData data, int x0, int y0, int w, int h, int layer) {
        try {
            var arr = data.GetDetailLayer(x0, y0, w, h, layer);
            if (arr == null) return null;
            int[] result = new int[w * h];
            int* src = (int*)((byte*)arr.Pointer + 4 * IntPtr.Size);
            for (int i = 0; i < result.Length; i++) result[i] = src[i];
            return result;
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[PuzzleTerrainHole] reading shrubs/grass failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    // Calls the game's TerrainData.SetDetailLayer(int, int, int, int[,]); the Il2Cpp bindings can't (2D array)
    private static unsafe bool WriteDetails(TerrainData data, int x0, int y0, int w, int h, int layer, int[] values) {
        try {
            if (!setDetailLooked) {
                setDetailLooked = true;
                IntPtr intClass = IL2CPP.GetIl2CppClass("mscorlib.dll", "System", "Int32");
                if (intClass != IntPtr.Zero) int2DArray = IL2CPP.il2cpp_bounded_array_class_get(intClass, 2, false);
                IntPtr klass = Il2CppClassPointerStore<TerrainData>.NativeClassPtr;
                setDetail = IL2CPP.il2cpp_class_get_method_from_name(klass, "SetDetailLayer", 4);
            }
            if (setDetail == IntPtr.Zero || int2DArray == IntPtr.Zero) return false;
            ulong[] lengths = { (ulong)h, (ulong)w };
            ulong[] lower = { 0UL, 0UL };
            IntPtr array = IL2CPP.il2cpp_array_new_full(int2DArray, ref lengths[0], ref lower[0]);
            if (array == IntPtr.Zero) return false;
            int* dst = (int*)((byte*)array + 4 * IntPtr.Size);
            for (int i = 0; i < values.Length; i++) dst[i] = values[i];
            int bx = x0, by = y0, bl = layer;
            void** args = stackalloc void*[4];
            args[0] = &bx; args[1] = &by; args[2] = &bl; args[3] = (void*)array;
            IntPtr exc = IntPtr.Zero;
            IL2CPP.il2cpp_runtime_invoke(setDetail, data.Pointer, args, ref exc);
            return exc == IntPtr.Zero;
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[PuzzleTerrainHole] writing shrubs/grass failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    // ---- trees ----

    private int RemoveTrees(Terrain terrain, Bounds area) {
        TerrainData data = terrain.terrainData;
        Vector3 size = data.size;
        Vector3 origin = terrain.transform.position;
        var all = data.treeInstances;
        if (all == null || all.Length == 0) return 0;

        var keep = new List<TreeInstance>(all.Length);
        var gone = new List<TreeInstance>();
        Bounds wide = area;
        wide.Expand(2f);
        for (int i = 0; i < all.Length; i++) {
            TreeInstance t = all[i];
            Vector3 world = origin + new Vector3(t.position.x * size.x, t.position.y * size.y, t.position.z * size.z);
            // a tree is hit when the collider reaches its trunk (at the base or about a metre up)
            if (wide.Contains(world) && (Touches(world, 0.5f) || Touches(world + Vector3.up, 0.5f))) gone.Add(t);
            else keep.Add(t);
        }
        if (gone.Count == 0) return 0;

        var arr = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<TreeInstance>(keep.Count);
        for (int i = 0; i < keep.Count; i++) arr[i] = keep[i];
        data.SetTreeInstances(arr, true);
        foreach (TreeInstance t in gone) removedTrees.Add((data, t));
        return gone.Count;
    }

    // ---- rocks (separate objects in the world, found by name) ----

    private static bool RockName(string n) {
        n = n.ToLowerInvariant();
        return n.Contains("rock") || n.Contains("boulder") || n.Contains("stone") || n.Contains("cliff");
    }

    private int RemoveRocks(Bounds area) {
        int count = 0;
        var hits = Physics.OverlapBox(area.center, area.extents, Quaternion.identity);
        if (hits == null) return 0;
        foreach (Collider other in hits) {
            if (other == null || other.isTrigger || other.TryCast<TerrainCollider>() != null) continue;
            GameObject rock = RockRoot(other.transform);
            if (rock == null || !rock.activeSelf || hiddenRocks.Contains(rock)) continue;
            if (!Overlaps(other)) continue;
            rock.SetActive(false);
            hiddenRocks.Add(rock);
            count++;
        }
        return count;
    }

    // The highest object up the chain (a few levels) whose name says rock, skipping anything that belongs to a puzzle, a player or a carried item
    private static GameObject RockRoot(Transform t) {
        GameObject found = null;
        Transform cur = t;
        for (int i = 0; i < 4 && cur != null; i++, cur = cur.parent) {
            if (cur.GetComponent<Prop>() != null || cur.GetComponent(Il2CppType.Of<PuzzleSDK>()) != null) return null;
            if (RockName(cur.name)) found = cur.gameObject;
        }
        if (found == null) return null;
        if (found.GetComponentInParent(Il2CppType.Of<PuzzleSDK>()) != null) return null;
        return found;
    }

    private bool Overlaps(Collider other) {
        foreach (Collider c in colliders) {
            if (c == null || !c.enabled) continue;
            try {
                if (Physics.ComputePenetration(c, c.transform.position, c.transform.rotation, other, other.transform.position, other.transform.rotation, out Vector3 dir, out float dist)) return true;
            } catch (Exception) {
                // not every collider pair supports it (e.g. two non-convex meshes): fall back to the bounds
                if (c.bounds.Intersects(other.bounds)) return true;
            }
        }
        return false;
    }

    // ---- put it all back ----

    private void OnDestroy() {
        try {
            foreach (Cut cut in cuts)
                if (cut.terrain != null && cut.data != null) Paint(cut, 1f - cut.holeValue);
            cuts.Clear();
            foreach (DetailCut d in detailCuts) if (d.data != null) WriteDetails(d.data, d.x0, d.y0, d.w, d.h, d.layer, d.original);
            detailCuts.Clear();

            // trees: put the removed ones back on top of whatever is there now
            var byData = new Dictionary<IntPtr, (TerrainData data, List<TreeInstance> trees)>();
            foreach (var r in removedTrees) {
                if (r.data == null) continue;
                if (!byData.TryGetValue(r.data.Pointer, out var e)) { e = (r.data, new List<TreeInstance>()); byData[r.data.Pointer] = e; }
                e.trees.Add(r.tree);
            }
            foreach (var e in byData.Values) {
                var now = e.data.treeInstances;
                int n = now == null ? 0 : now.Length;
                var arr = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<TreeInstance>(n + e.trees.Count);
                for (int i = 0; i < n; i++) arr[i] = now[i];
                for (int i = 0; i < e.trees.Count; i++) arr[n + i] = e.trees[i];
                e.data.SetTreeInstances(arr, true);
            }
            removedTrees.Clear();

            foreach (GameObject rock in hiddenRocks) if (rock != null) rock.SetActive(true);
            hiddenRocks.Clear();
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[PuzzleTerrainHole] '{name}' could not restore the terrain: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

} // namespace BigPuzzleSDK
