using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace BigPuzzleSDK {

// The game's own materials in a puzzle. A puzzlepack holds nothing of the game's: the creator uses the
// SDK's Game Materials (see GameMaterialLooks), and the loader swaps each of them for the game's material
// of that name, found in the player's own game. PuzzleGlass looks its materials up here too.
internal static class GameMaterials {
    private static readonly Dictionary<string, Material> found = new Dictionary<string, Material>();

    // The game's material with this name, as loaded from its files, or null when the game has none.
    // The game also makes runtime copies with the same name (150 of MetalShiny_Vertex in a test session), and a copy
    // can render differently: MetalShiny_Vertex copies render black. Unity gives an object loaded from
    // the game's files a positive instance id, and an object made at runtime a negative one.
    internal static Material Find(string name) {
        if (found.TryGetValue(name, out Material known) && known != null) return known;
        foreach (Object o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Material>())) {
            Material m = o.TryCast<Material>();
            if (m != null && m.name == name && m.GetInstanceID() > 0) { found[name] = m; return m; }
        }
        return null;
    }

    // Swaps every Game Material on the creator's renderers under 'root' for the game's material. A mesh with
    // a Game Material that paints gets a copy with the game's vertex colours and UVs (GameMaterialLooks.Paint).
    // The loader calls it before it adds any of the game's objects to the puzzle.
    internal static void ApplyTo(GameObject root) {
        foreach (Component c in root.GetComponentsInChildren(Il2CppType.Of<Renderer>(), true)) {
            Renderer r = c.TryCast<Renderer>();
            if (r == null) continue;
            var mats = r.sharedMaterials;
            var paints = new GameMaterialLooks.SubmeshPaint[mats.Length];
            bool swapped = false, painted = false;
            for (int i = 0; i < mats.Length; i++) {
                if (!GameMaterialLooks.TryGetType(mats[i], out GameMaterialType type)) continue;
                GameMaterialLook look = GameMaterialLooks.Of(type);
                Material game = Find(look.material);
                if (game == null) {
                    Plugin.log.LogWarning($"[GameMaterial] The game has no material '{look.material}', so '{r.name}' keeps the SDK's '{mats[i].name}'.");
                    continue;
                }
                if (look.paints) {
                    paints[i] = new GameMaterialLooks.SubmeshPaint(GameMaterialLooks.PaintFor(look, mats[i].GetColor(GameMaterialLooks.ColorProperty)), look.uvPerMetre);
                    painted = true;
                }
                mats[i] = game;
                swapped = true;
            }
            if (!swapped) continue;
            if (painted && !PaintMesh(r, paints)) continue;
            r.sharedMaterials = mats;
        }
    }

    // Gives the renderer a painted copy of its mesh. False (and a warning) when the mesh can't be painted.
    private static bool PaintMesh(Renderer r, GameMaterialLooks.SubmeshPaint[] paints) {
        MeshFilter filter = r.GetComponent<MeshFilter>();
        Mesh mesh = filter != null ? filter.sharedMesh : null;
        if (mesh == null || !mesh.isReadable) {
            Plugin.log.LogWarning($"[GameMaterial] '{r.name}' keeps the SDK's materials: Game Materials other than glass need a Mesh Renderer with a readable mesh. Turn on Read/Write in the model's import settings.");
            return false;
        }
        Mesh copy = Object.Instantiate(mesh).Cast<Mesh>();
        copy.name = mesh.name + " (Game Material)";
        GameMaterialLooks.Paint(copy, r.transform.lossyScale, paints);
        filter.sharedMesh = copy;
        return true;
    }
}

} // namespace BigPuzzleSDK
