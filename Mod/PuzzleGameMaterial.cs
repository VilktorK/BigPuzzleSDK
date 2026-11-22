using System;
using System.Collections.Generic;
using UnityEngine;

namespace BigPuzzleSDK {

// Runtime (mod) version of the SDK's PuzzleGameMaterial: the creator's own meshes get one of the
// materials the game builds its structures with. The material is looked up in the player's game.
//
// How the game does it: its buildings use 'Better Lit' materials (and the plain househouse/VertexColors)
// that multiply the texture by the mesh's vertex colours and map it by UV0, at a fixed number of UV
// units per metre. So each mesh gets a copy with the chosen colour as its vertex colours, and UVs
// projected onto its box sides at the game's own scale (GameMaterialLooks, shared with the editor).
public class PuzzleGameMaterial : MonoBehaviour {
    public PuzzleGameMaterial(IntPtr ptr) : base(ptr) { }

    // Set in the Inspector; the names must match the Unity-side fields
    internal PuzzleGameMaterialType material;
    internal Color color = Color.white;   // white = the game's usual colour for this material

    private bool set;

    // Runs when the puzzle is switched on, after the loader has put the game's models under the other SDK
    // components: Paints skips those, so only the creator's own meshes are painted
    private void Awake() {
        if (set) return;
        set = true;
        GameMaterialLook look = GameMaterialLooks.Of(material);
        Material game = GameMaterials.Find(look.material);
        if (game == null) {
            Plugin.log.LogWarning($"[PuzzleGameMaterial] the game's '{look.material}' material wasn't found; '{name}' keeps its own material.");
            return;
        }
        Color32 paint = GameMaterialLooks.PaintFor(look, color);
        foreach (MeshRenderer r in GetComponentsInChildren<MeshRenderer>(true)) {
            if (!Paints(r.transform)) continue;
            MeshFilter filter = r.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null) continue;
            if (!mesh.isReadable) {
                Plugin.log.LogWarning($"[PuzzleGameMaterial] '{r.name}' keeps its own material: its mesh '{mesh.name}' can't be read in game. Turn on Read/Write in its model's import settings.");
                continue;
            }
            Mesh copy = UnityEngine.Object.Instantiate(mesh).Cast<Mesh>();
            copy.name = mesh.name + " (PuzzleGameMaterial)";
            GameMaterialLooks.Paint(copy, r.transform.lossyScale, paint, look.uvPerMetre);
            filter.sharedMesh = copy;
            int slots = Math.Max(1, r.sharedMaterials.Length);
            var mats = new Material[slots];
            for (int i = 0; i < slots; i++) mats[i] = game;
            r.sharedMaterials = mats;
        }
    }

    // Whether this Game Material paints the meshes on 't': no nearer Game Material, and no other SDK
    // component that owns its look, on 't' or between it and this object (the editor's PaintedMeshes too)
    private bool Paints(Transform t) {
        for (; t != null; t = t.parent) {
            foreach (MonoBehaviour mb in t.GetComponents<MonoBehaviour>()) {
                if (mb == null || mb.Pointer == Pointer) continue;
                Il2CppSystem.Type type = mb.GetIl2CppType();
                if (!SdkComponents().Contains(type.FullName)) continue;
                if (type.Name == nameof(PuzzleGameMaterial) || !GameMaterialLooks.PaintsThrough(type.Name)) return false;
            }
            if (t.Pointer == transform.Pointer) return true;
        }
        return false;
    }

    // The full names of the SDK's components: the MonoBehaviours of this mod, which the loader registers
    // with the game. (Not all of them are in the BigPuzzleSDK namespace: PuzzleButton is not.)
    private static HashSet<string> sdkComponents;

    private static HashSet<string> SdkComponents() {
        if (sdkComponents != null) return sdkComponents;
        sdkComponents = new HashSet<string>();
        foreach (Type t in typeof(PuzzleGameMaterial).Assembly.GetTypes())
            if (typeof(MonoBehaviour).IsAssignableFrom(t)) sdkComponents.Add(t.FullName);
        return sdkComponents;
    }
}

} // namespace BigPuzzleSDK
