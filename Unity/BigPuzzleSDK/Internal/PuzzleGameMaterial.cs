// Inspector fields here are read by the BigPuzzleSDK mod in-game, not by this script
#pragma warning disable 0414

using System.Collections.Generic;
using UnityEngine;

namespace BigPuzzleSDK {

// Gives the meshes on this object, and under it, one of the materials the game builds its structures
// with. Use any mesh, at any size (scaled cubes make walls, floors and frames). In-game each mesh gets
// the game's material, painted in Color, with the texture at the game's own scale (GameMaterialLooks).
// The material set here doesn't matter: it's replaced in-game, and the editor shows a preview.
[PuzzleInfo("Gives the meshes on this object, and under it, one of the game's structure materials: painted, wood, concrete, metal and more. Any shape or size.")]
[AddComponentMenu("Big Puzzle SDK/Game Material")]
[DisallowMultipleComponent]
public class PuzzleGameMaterial : MonoBehaviour {
    [Tooltip("Painted: the game's plain flat colour (most walls, and the window frames). Metal Grate, Wire Mesh and Speaker Grille have holes. Some materials ignore Color: see the Game Material section of the guide.")]
    [SerializeField] private PuzzleGameMaterialType material = PuzzleGameMaterialType.Concrete;

    [Tooltip("The paint. White (#FFFFFF) keeps the game's usual colour for this material (brown wood, dark red carpet, red metal grate, white for Painted and Concrete).")]
    [SerializeField] private Color color = Color.white;

    internal PuzzleGameMaterialType Material => material;
    internal Color Color => color;

    // The meshes it paints, as the mod picks them (PuzzleGameMaterial.Paints in the mod): on it or under it,
    // with no nearer Game Material and no other SDK component that owns its look on them or between
    // (GameMaterialLooks.PaintsThrough names the SDK components it paints through)
    internal List<MeshFilter> PaintedMeshes(bool includeInactive) {
        var list = new List<MeshFilter>();
        foreach (MeshFilter mf in GetComponentsInChildren<MeshFilter>(includeInactive))
            if (mf.sharedMesh != null && mf.GetComponent<MeshRenderer>() != null && Paints(mf.transform)) list.Add(mf);
        return list;
    }

    private bool Paints(Transform t) {
        for (; t != null; t = t.parent) {
            foreach (MonoBehaviour mb in t.GetComponents<MonoBehaviour>()) {
                if (mb == null || mb == this) continue;
                System.Type type = mb.GetType();
                if (type.Assembly != typeof(PuzzleGameMaterial).Assembly) continue;   // not an SDK component
                if (mb is PuzzleGameMaterial || !GameMaterialLooks.PaintsThrough(type.Name)) return false;
            }
            if (t == transform) return true;
        }
        return false;
    }
}

}
