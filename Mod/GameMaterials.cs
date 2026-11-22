using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace BigPuzzleSDK {

// The game's own materials, found by name in the player's game when a puzzle loads. A puzzlepack holds
// nothing of the game's, so PuzzleGlass and PuzzleGameMaterial look their materials up here.
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
}

} // namespace BigPuzzleSDK
