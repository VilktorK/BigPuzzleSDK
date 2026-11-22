// BigPuzzleSDK: what the Game Materials (BigPuzzleSDK/Materials) look like in the editor. Their shader,
// "BigPuzzleSDK/Game Material", reads each game material's colours, texture scale and texture from the
// global shader values set here from GameMaterialLooks. So a material asset holds only which game material
// it is and its paint, and a duplicated material looks right at once.
//
// The textures come from the creator's own game (BigWalkGameModels) and live in memory only.
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BigPuzzleSDK.GameAssets {

[InitializeOnLoad]
public static class GameMaterialPreview {
    private const int Slots = 64;             // the arrays' length in GameMaterial.shader
    private const int TextureSize = 512;      // each game texture's size in the preview
    private static RenderTexture textures;    // one slice per name in GameMaterialLooks.Textures()

    static GameMaterialPreview() {
        Apply();
        BigWalkGameModels.Changed += Apply;
        AssemblyReloadEvents.beforeAssemblyReload += Release;
    }

    // Sets the global values the shader reads, with the game's textures when they are loaded
    private static void Apply() {
        string[] names = GameMaterialLooks.Textures();
        bool withTextures = BuildTextures(names);
        var own = new Vector4[Slots];
        var tint = new Vector4[Slots];
        var map = new Vector4[Slots];
        for (int i = 0; i < GameMaterialLooks.Count; i++) {
            GameMaterialLook look = GameMaterialLooks.Of((GameMaterialType)i);
            own[i] = new Vector4(look.own.r / 255f, look.own.g / 255f, look.own.b / 255f, look.usesColor ? 1f : 0f);
            tint[i] = new Vector4(look.tint.r / 255f, look.tint.g / 255f, look.tint.b / 255f, look.opacity);
            bool textured = withTextures && look.texture != null && BigWalkGameModels.GetTexture(look.texture) != null;
            map[i] = new Vector4(look.uvPerMetre * look.tileX, look.uvPerMetre * look.tileY, look.cutoff, textured ? Array.IndexOf(names, look.texture) : -1f);
        }
        Shader.SetGlobalVectorArray("_BigWalkOwn", own);
        Shader.SetGlobalVectorArray("_BigWalkTint", tint);
        Shader.SetGlobalVectorArray("_BigWalkMap", map);
    }

    // Scales each loaded game texture into its slice of a texture array. False when there is no GPU
    // (a -nographics batch run) or no game texture is loaded.
    private static bool BuildTextures(string[] names) {
        Release();
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return false;
        bool any = false;
        foreach (string name in names) if (BigWalkGameModels.GetTexture(name) != null) any = true;
        if (!any) return false;

        var descriptor = new RenderTextureDescriptor(TextureSize, TextureSize, RenderTextureFormat.ARGB32, 0) {
            dimension = TextureDimension.Tex2DArray, volumeDepth = names.Length, useMipMap = true, autoGenerateMips = false, sRGB = true,
        };
        textures = new RenderTexture(descriptor) {
            name = "BigPuzzleSDK game material textures", hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4,
        };
        textures.Create();
        for (int i = 0; i < names.Length; i++) {
            Texture2D source = BigWalkGameModels.GetTexture(names[i]);
            if (source != null) Graphics.Blit(source, textures, 0, i);
        }
        textures.GenerateMips();
        Shader.SetGlobalTexture("_BigWalkGameTextures", textures);
        return true;
    }

    private static void Release() {
        if (textures == null) return;
        textures.Release();
        UnityEngine.Object.DestroyImmediate(textures);
        textures = null;
    }

    // The render state a Game Material needs: glass is see-through and seen from both sides
    internal static void Configure(Material material) {
        if (!GameMaterialLooks.TryGetType(material, out GameMaterialType type)) return;
        bool seeThrough = GameMaterialLooks.Of(type).opacity < 1f;
        material.SetFloat("_SrcBlend", (float)(seeThrough ? BlendMode.SrcAlpha : BlendMode.One));
        material.SetFloat("_DstBlend", (float)(seeThrough ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
        material.SetFloat("_ZWrite", seeThrough ? 0f : 1f);
        material.SetFloat("_Cull", (float)(seeThrough ? CullMode.Off : CullMode.Back));
        material.SetOverrideTag("RenderType", seeThrough ? "Transparent" : "");
        material.renderQueue = seeThrough ? (int)RenderQueue.Transparent : -1;   // -1: the shader's own (Geometry)
    }
}

// The Inspector of a Game Material: which game material it stands for, and its paint
public class GameMaterialInspector : ShaderGUI {
    private static readonly string[] Names = Array.ConvertAll(Enum.GetNames(typeof(GameMaterialType)), ObjectNames.NicifyVariableName);

    public override void OnGUI(MaterialEditor editor, MaterialProperty[] properties) {
        MaterialProperty type = FindProperty(GameMaterialLooks.TypeProperty, properties);
        MaterialProperty color = FindProperty(GameMaterialLooks.ColorProperty, properties);
        EditorGUI.showMixedValue = type.hasMixedValue;
        EditorGUI.BeginChangeCheck();
        int value = EditorGUILayout.Popup("Game Material", Mathf.RoundToInt(type.floatValue), Names);
        if (EditorGUI.EndChangeCheck()) type.floatValue = value;
        EditorGUI.showMixedValue = false;
        if (type.hasMixedValue || value < 0 || value >= Names.Length) {
            editor.ColorProperty(color, "Paint");
            return;
        }

        GameMaterialLook look = GameMaterialLooks.Of((GameMaterialType)value);
        using (new EditorGUI.DisabledScope(!look.usesColor)) editor.ColorProperty(color, "Paint");
        string text = $"In-game this becomes the game's '{look.material}' material.\n";
        text += look.usesColor ? "White keeps its usual colour. Duplicate this material (Ctrl+D) to paint it another colour.\n" : "It has its own colour, so Paint does nothing.\n";
        text += look.paints
            ? "Use it on a Mesh Renderer whose mesh has Read/Write on (Unity's own shapes have it). The texture follows the object's size, as in-game."
            : "It changes only the look. For glass that muffles voices, use the Glass component.";
        EditorGUILayout.HelpBox(text, MessageType.Info);
    }

    public override void ValidateMaterial(Material material) => GameMaterialPreview.Configure(material);
}

} // namespace BigPuzzleSDK.GameAssets
