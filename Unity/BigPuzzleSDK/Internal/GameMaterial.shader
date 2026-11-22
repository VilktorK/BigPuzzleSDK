// BigPuzzleSDK: the shader of the Game Materials in BigPuzzleSDK/Materials. In-game the mod swaps each of
// these materials for the game's own material (GameMaterials.ApplyTo), so this shader draws only the
// editor's preview of it.
//
// A material holds only which game material it is (_GameMaterial) and its paint (_Color). The rest comes
// from global values that the editor sets from GameMaterialLooks (GameMaterialPreview): the usual colour,
// the tint, the texture scale and cutoff, and the game's texture, read from the creator's own install.
// The texture is mapped like the mod paints the mesh: onto the side each vertex faces, in metres.
Shader "BigPuzzleSDK/Game Material" {
    Properties {
        _GameMaterial ("Game Material", Float) = 5
        _Color ("Paint", Color) = (1, 1, 1, 1)
        [HideInInspector] _SrcBlend ("", Float) = 1
        [HideInInspector] _DstBlend ("", Float) = 0
        [HideInInspector] _ZWrite ("", Float) = 1
        [HideInInspector] _Cull ("", Float) = 2   // 2 = hide back faces, 0 = show both sides (glass)
    }
    SubShader {
        Tags { "RenderType" = "Opaque" }
        Pass {
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma require 2darray
            #include "UnityCG.cginc"

            float _GameMaterial;
            float4 _Color;
            UNITY_DECLARE_TEX2DARRAY(_BigWalkGameTextures);
            float4 _BigWalkOwn[64];    // the usual vertex colour (rgb), and 1 when the paint counts (a)
            float4 _BigWalkTint[64];   // what the material multiplies in (rgb), and the opacity (a)
            float4 _BigWalkMap[64];    // texture repeats per metre (xy), alpha cutoff (z), texture slice or -1 (w)

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 normal : TEXCOORD0; float2 uv : TEXCOORD1; };

            int Look() { return clamp((int)(_GameMaterial + 0.5), 0, 63); }

            v2f vert (appdata v) {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                // As GameMaterialLooks.Paint: the object's own axes in metres, onto the side the vertex faces
                float3 scale = float3(length(unity_ObjectToWorld._m00_m10_m20), length(unity_ObjectToWorld._m01_m11_m21), length(unity_ObjectToWorld._m02_m12_m22));
                float3 p = v.vertex.xyz * scale;
                float3 n = abs(v.normal / max(scale, 1e-6));
                float2 side = (n.x >= n.y && n.x >= n.z) ? p.zy : ((n.y >= n.z) ? p.xz : p.xy);
                o.uv = side * _BigWalkMap[Look()].xy;
                return o;
            }

            float4 frag (v2f i) : SV_Target {
                int k = Look();
                float4 own = _BigWalkOwn[k], tint = _BigWalkTint[k], map = _BigWalkMap[k];
                float3 paint = _Color.rgb;
                #ifndef UNITY_COLORSPACE_GAMMA
                paint = LinearToGammaSpace(paint);   // the mod writes the paint as set, like the game's vertex colours
                #endif
                bool white = all(paint > 0.998);
                float4 c = float4((own.a > 0.5 && !white ? paint : own.rgb) * tint.rgb, tint.a);
                if (map.w >= 0) {
                    float4 t = UNITY_SAMPLE_TEX2DARRAY(_BigWalkGameTextures, float3(i.uv, map.w));
                    if (map.z > 0) clip(t.a - map.z);   // grilles and wire mesh, as the game cuts them
                    c.rgb *= t.rgb;
                }
                float3 n = normalize(i.normal);
                float3 sun = normalize(float3(0.35, 0.8, 0.45));
                float light = 0.45 + 0.55 * saturate(dot(n, sun)) + 0.1 * saturate(n.y);
                return float4(c.rgb * light, c.a);
            }
            ENDCG
        }
    }
    CustomEditor "BigPuzzleSDK.GameAssets.GameMaterialInspector"
}
