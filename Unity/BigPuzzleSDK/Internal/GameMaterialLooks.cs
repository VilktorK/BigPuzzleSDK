// BigPuzzleSDK: the game's structure materials and glass. The Unity package and the mod compile this
// same file (Mod/BigPuzzleSDK.csproj links it), so the editor preview and the game use the same numbers.
//
// A creator uses them as Unity materials: the materials in BigPuzzleSDK/Materials use the shader
// "BigPuzzleSDK/Game Material", and their _GameMaterial property says which game material they stand for.
// When a puzzle loads, the mod swaps each of them for the game's own material (GameMaterials.ApplyTo).
//
// Nothing of the game's is here: the mod finds each material by its name in the player's own game, and
// the editor reads the preview textures, by name, from the creator's own install (BigWalkGameModels).
using UnityEngine;

namespace BigPuzzleSDK {

// Which game material. A material stores it as a number in _GameMaterial: add new ones at the end.
public enum GameMaterialType {
    Painted, GlowingPaint, Black,
    Wood, Plywood,
    Concrete, ConcretePoured, ConcreteGrid, ConcreteYellow, ConcreteRed, ConcreteBlue, ConcreteGreen, ConcreteWarm,
    Metal, MetalPole, PaintedMetal, RustyMetal, MetalGrate, WireMesh, SpeakerGrille,
    Tile, Carpet, Cork, FoamRubber, Rock, PowerCord,
    OneWayMirror, GlassWindow, GlassRippled, GlassClear
}

// One game material, measured on the game's own meshes
internal sealed class GameMaterialLook {
    public readonly string material;     // the game's material, which the mod looks up by name
    public readonly bool paints;         // the mesh needs the game's vertex colours and UVs (glass does not)
    public readonly Color32 own;         // the most common vertex colour on the game's meshes: what white paint means
    public readonly bool usesColor;      // false: the game's material ignores vertex colours
    public readonly float uvPerMetre;    // median UV units per metre on the game's meshes (1 / side for the cube-only ones)
    // Editor preview only
    public readonly string texture;      // the albedo texture, or null when the preview shows plain colour
    public readonly float tileX, tileY;  // the material's texture tiling
    public readonly Color32 tint;        // what the material itself multiplies in (its _Tint, or the _RColor of a tint mask)
    public readonly float cutoff;        // > 0: the texture's alpha cuts holes below this
    public readonly float opacity;       // < 1: see-through (glass)

    private GameMaterialLook(string material, bool paints, Color32 own, bool usesColor, float uvPerMetre, string texture, float tileX, float tileY, Color32 tint, float cutoff, float opacity) {
        this.material = material; this.paints = paints; this.own = own; this.usesColor = usesColor; this.uvPerMetre = uvPerMetre;
        this.texture = texture; this.tileX = tileX; this.tileY = tileY; this.tint = tint; this.cutoff = cutoff; this.opacity = opacity;
    }

    // A material the game puts on vertex-coloured meshes with its UVs at a fixed scale
    internal static GameMaterialLook Surface(string material, Color32 own, bool usesColor, float uvPerMetre, string texture, float tileX, float tileY, Color32 tint, float cutoff) =>
        new GameMaterialLook(material, true, own, usesColor, uvPerMetre, texture, tileX, tileY, tint, cutoff, 1f);

    // The game's puzzle glass (shader househouse/Glass): any mesh as it is
    internal static GameMaterialLook Glass(string material) =>
        new GameMaterialLook(material, false, new Color32(221, 229, 245, 255), false, 0f, null, 1f, 1f, new Color32(255, 255, 255, 255), 0f, 0.22f);
}

internal static class GameMaterialLooks {
    // What a material asset must have for the mod to swap it
    internal const string ShaderName = "BigPuzzleSDK/Game Material";
    internal const string TypeProperty = "_GameMaterial";
    internal const string ColorProperty = "_Color";

    private static readonly Color32 White = new Color32(255, 255, 255, 255);
    private static Color32 C(byte r, byte g, byte b) => new Color32(r, g, b, 255);

    // In the order of GameMaterialType
    private static readonly GameMaterialLook[] All = {
        GameMaterialLook.Surface("hhVertexColors", White, true, 0.0375f, null, 1, 1, White, 0f),
        GameMaterialLook.Surface("hhVertexColorsEmissive", White, true, 0.2449f, null, 1, 1, White, 0f),
        GameMaterialLook.Surface("PlaceholderBlack", White, false, 3.1746f, null, 1, 1, C(16, 16, 16), 0f),
        GameMaterialLook.Surface("WoodGrain_VertexColour", C(168, 59, 23), true, 0.0317f, "Wood047_2K_Displacement", 5, 10, White, 0f),
        GameMaterialLook.Surface("WoodPly", White, false, 0.0227f, "plywood_diff_2k", 10, 10, White, 0f),
        GameMaterialLook.Surface("Concrete_Mottle_Vertex", White, true, 0.0313f, "ConcreteMottle", 10, 10, White, 0f),
        GameMaterialLook.Surface("Concrete_Poured_Vertex", White, true, 1.2224f, null, 1, 1, White, 0f),
        GameMaterialLook.Surface("Concrete_Grid", White, false, 0.2f, null, 1, 1, C(195, 195, 195), 0f),
        GameMaterialLook.Surface("PlaceholderYellow_Concrete_Mottle", White, false, 0.5f, "ConcreteMottle", 5, 5, C(255, 187, 26), 0f),
        GameMaterialLook.Surface("PlaceholderRed_Concrete_Mottle", White, true, 0.3942f, null, 1, 1, C(245, 103, 103), 0f),
        GameMaterialLook.Surface("PlaceholderBlue_Concrete_Mottle", White, false, 0.503f, "ConcreteMottle", 5, 5, C(48, 135, 255), 0f),
        GameMaterialLook.Surface("PlaceholderGreen_Concrete_Mottle", White, false, 0.2f, "ConcreteMottle", 5, 5, C(0, 169, 150), 0f),
        GameMaterialLook.Surface("PlaceholderNeutralWarm_Concrete_Mottle", White, true, 0.3039f, null, 1, 1, C(243, 227, 192), 0f),
        GameMaterialLook.Surface("MetalShiny_Vertex", C(38, 10, 5), true, 0.0355f, null, 1, 1, White, 0f),
        GameMaterialLook.Surface("MetalPole", C(151, 151, 151), true, 0.0312f, null, 1, 1, White, 0f),
        GameMaterialLook.Surface("MetalPainted", White, false, 1.6047f, "MetalPainted_BaseColor", 1, 1, White, 0f),
        GameMaterialLook.Surface("Metal_Rusty", White, false, 0.128f, "Metal013_1K_Color", 8, 8, White, 0f),
        GameMaterialLook.Surface("metal-grille", C(204, 16, 19), true, 0.0314f, "stairMesh-Alpha", 100, 100, White, 0.5f),
        GameMaterialLook.Surface("MetalWireFrame", C(204, 125, 18), true, 0.0325f, "metalwire_HiRes_AlbedoAlpha", 78.9f, 78.9f, White, 0.382f),
        GameMaterialLook.Surface("SpeakerGrille", White, false, 3.26f, "circle", 50, 50, C(16, 16, 16), 0.507f),
        GameMaterialLook.Surface("Tile_VertexColour", C(15, 18, 81), true, 0.0314f, "Tiles105_2K-JPG_Color", 30, 30, White, 0f),
        GameMaterialLook.Surface("Carpet_VertexColour", C(33, 4, 2), true, 0.0246f, "Carpet013_1K-JPG_Color", 30, 30, White, 0f),
        GameMaterialLook.Surface("Cork_VertexColour", C(142, 85, 35), true, 1.111f, "Cork_BaseColor", 2, 2, White, 0f),
        GameMaterialLook.Surface("FoamRubber_VertexColour", C(29, 96, 14), true, 0.0429f, "Rubber003_2K-JPG_Color", 15, 15, White, 0f),
        GameMaterialLook.Surface("Rock-Tiled", White, false, 0.0729f, "granite-detail_basecolor", 1, 1, C(255, 242, 230), 0f),
        GameMaterialLook.Surface("PowerCord-Green", White, false, 0.966f, null, 1, 1, C(38, 47, 28), 0f),
        GameMaterialLook.Surface("Mirror_OneWay_Black", White, false, 0.0374f, null, 1, 1, C(16, 16, 16), 0f),
        GameMaterialLook.Glass("hhGlass"),           // most windows
        GameMaterialLook.Glass("hhGlass_hifreq"),    // the wavier glass of the gourd boxes
        GameMaterialLook.Glass("hhGlass_lowfreq"),   // barely distorts
    };

    internal static int Count => All.Length;

    internal static GameMaterialLook Of(GameMaterialType type) => All[(int)type];

    // The game material a material asset stands for. False: it is not one (another shader, or a number
    // outside GameMaterialType).
    internal static bool TryGetType(Material material, out GameMaterialType type) {
        type = default;
        if (material == null || material.shader == null || material.shader.name != ShaderName) return false;
        int value = Mathf.RoundToInt(material.GetFloat(TypeProperty));
        if (value < 0 || value >= All.Length) return false;
        type = (GameMaterialType)value;
        return true;
    }

    // The preview textures, each once, in a fixed order (the editor's texture array follows it)
    internal static string[] Textures() {
        var names = new System.Collections.Generic.List<string>();
        foreach (GameMaterialLook look in All) if (look.texture != null && !names.Contains(look.texture)) names.Add(look.texture);
        return names.ToArray();
    }

    // The vertex colour of a mesh painted in 'chosen': white keeps the material's usual colour
    internal static Color32 PaintFor(GameMaterialLook look, Color chosen) => !look.usesColor || IsWhite(chosen) ? look.own : (Color32)chosen;

    internal static bool IsWhite(Color c) => c.r > 0.998f && c.g > 0.998f && c.b > 0.998f;

    // How one submesh is painted
    internal struct SubmeshPaint {
        public bool painted;
        public Color32 color;
        public float uvPerMetre;
        public SubmeshPaint(Color32 color, float uvPerMetre) { painted = true; this.color = color; this.uvPerMetre = uvPerMetre; }
    }

    // Paints a mesh like the game's structures. The vertices of each painted submesh get its colour, and
    // UVs projected onto the side each vertex faces, in metres (the object's scale included) times the game's
    // UV units per metre. The other vertices keep their colour (white when the mesh has none) and their UVs.
    internal static void Paint(Mesh mesh, Vector3 scale, SubmeshPaint[] submeshes) {
        if (mesh.normals.Length != mesh.vertexCount) mesh.RecalculateNormals();
        var vertices = mesh.vertices;
        var normals = mesh.normals;
        var oldColors = mesh.colors32;
        var oldUv = mesh.uv;
        int count = vertices.Length;
        var colors = new Color32[count];
        var uv = new Vector2[count];
        for (int i = 0; i < count; i++) {
            colors[i] = oldColors.Length == count ? oldColors[i] : White;
            if (oldUv.Length == count) uv[i] = oldUv[i];
        }
        scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        for (int s = 0; s < submeshes.Length && s < mesh.subMeshCount; s++) {
            if (!submeshes[s].painted) continue;
            var indices = mesh.GetIndices(s);
            for (int k = 0; k < indices.Length; k++) {
                int i = indices[k];
                colors[i] = submeshes[s].color;
                Vector3 p = Vector3.Scale(vertices[i], scale);
                Vector3 n = normals[i];
                n = new Vector3(Mathf.Abs(n.x / Mathf.Max(scale.x, 1e-6f)), Mathf.Abs(n.y / Mathf.Max(scale.y, 1e-6f)), Mathf.Abs(n.z / Mathf.Max(scale.z, 1e-6f)));
                Vector2 side = n.x >= n.y && n.x >= n.z ? new Vector2(p.z, p.y) : n.y >= n.z ? new Vector2(p.x, p.z) : new Vector2(p.x, p.y);
                uv[i] = side * submeshes[s].uvPerMetre;
            }
        }
        mesh.colors32 = colors;
        mesh.uv = uv;
    }
}

} // namespace BigPuzzleSDK
