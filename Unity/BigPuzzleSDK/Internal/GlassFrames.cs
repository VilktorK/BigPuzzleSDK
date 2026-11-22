// BigPuzzleSDK: the frames a Glass can add around a pane, as meshes. The Unity package and the mod compile
// this same file (Mod/BigPuzzleSDK.csproj links it), so the editor preview shows the frame the game builds.
//
// A frame is built in metres, in the pane's own axes (through the glass, and along its two sides), around
// the centre of the pane mesh's bounds. ToMesh stores it in the pane mesh's own units, so a frame keeps its
// size in metres whatever the pane's scale.
using System.Collections.Generic;
using UnityEngine;

namespace BigPuzzleSDK {

// A Glass stores it as a number: add new ones at the end
public enum PuzzleGlassFrame { Painted, Rods }

internal static class GlassFrames {
    private const float Proud = 0.01f;            // metres a frame stands out from each face of the pane

    // The Rods frame, as measured on the glass wall of the game's Blindfold Catwalk (1.5 m panes, 8 cm thick)
    private const float BandWidth = 0.015f;       // the metal band around the pane's edges
    private const float BandOverlap = 0.005f;     // how far the band reaches over the glass
    private const float RodRadius = 0.03f;        // hexagonal rods, centre to corner
    private const float RodLength = 0.28f;
    private const float OuterWidth = 0.015f;      // the strips at the far ends of the rods

    // Painted: bars like the game's window frames, in its plain painted material, nearly black.
    // Rods: the metal fittings of the game's Blindfold Catwalk glass wall, orange-yellow.
    internal static GameMaterialType MaterialOf(PuzzleGlassFrame style) => style == PuzzleGlassFrame.Rods ? GameMaterialType.Metal : GameMaterialType.Painted;
    internal static Color32 OwnColor(PuzzleGlassFrame style) => style == PuzzleGlassFrame.Rods ? new Color32(204, 132, 4, 255) : new Color32(8, 8, 8, 255);

    // The frame's mesh, painted in 'color' (white = the style's own colour) like the game's structures
    internal static Mesh Build(PuzzleGlassFrame style, Bounds bounds, Vector3 scale, float width, Color color) {
        Mesh mesh = Shape(style, bounds, scale, width);
        GameMaterialLook look = GameMaterialLooks.Of(MaterialOf(style));
        Color32 paint = GameMaterialLooks.IsWhite(color) ? OwnColor(style) : (Color32)color;
        GameMaterialLooks.Paint(mesh, scale, new[] { new GameMaterialLooks.SubmeshPaint(paint, look.uvPerMetre) });
        return mesh;
    }

    private static Mesh Shape(PuzzleGlassFrame style, Bounds bounds, Vector3 scale, float width) {
        scale = new Vector3(Mathf.Max(Mathf.Abs(scale.x), 1e-6f), Mathf.Max(Mathf.Abs(scale.y), 1e-6f), Mathf.Max(Mathf.Abs(scale.z), 1e-6f));
        Vector3 size = Vector3.Scale(bounds.size, scale);
        int t = size.x <= size.y && size.x <= size.z ? 0 : (size.y <= size.z ? 1 : 2);   // through the glass
        int a = (t + 1) % 3, c = (t + 2) % 3;
        var parts = new Parts();
        float depth = size[t] + 2f * Proud;
        if (style == PuzzleGlassFrame.Rods) AddRods(parts, size, t, a, c, depth);
        else AddPainted(parts, size, t, a, c, depth, width);
        return parts.ToMesh(bounds.center, scale);
    }

    // Four bars, each 'width' wide, over the pane's edges: like the game's window frames
    private static void AddPainted(Parts parts, Vector3 size, int t, int a, int c, float depth, float width) {
        for (int side = -1; side <= 1; side += 2) {
            parts.Box(At(a, 0f, c, side * (size[c] - width) * 0.5f, t, 0f), At(a, size[a], c, width, t, depth));
            parts.Box(At(c, 0f, a, side * (size[a] - width) * 0.5f, t, 0f), At(c, size[c] - 2f * width, a, width, t, depth));
        }
    }

    // A metal band around the edges; two rods out of each side, at its thirds; and a strip across the rods'
    // far ends, with a block at each corner
    private static void AddRods(Parts parts, Vector3 size, int t, int a, int c, float depth) {
        float bandOut = BandWidth - BandOverlap;                 // the band's reach past the glass
        float bandCentre = BandWidth * 0.5f - BandOverlap;       // from the glass edge
        float outerCentre = bandOut + RodLength + OuterWidth * 0.5f;
        float rodCentre = bandOut + RodLength * 0.5f;
        for (int side = -1; side <= 1; side += 2) {
            // The band: the two bars along 'a' run past the corners, the two along 'c' fit between them
            parts.Box(At(a, 0f, c, side * (size[c] * 0.5f + bandCentre), t, 0f), At(a, size[a] + 2f * bandOut, c, BandWidth, t, depth));
            parts.Box(At(c, 0f, a, side * (size[a] * 0.5f + bandCentre), t, 0f), At(c, size[c] - 2f * BandOverlap, a, BandWidth, t, depth));
            for (int third = -1; third <= 1; third += 2) {
                parts.Rod(At(a, third * size[a] / 6f, c, side * (size[c] * 0.5f + rodCentre), t, 0f), c, a, t, RodRadius, RodLength);
                parts.Rod(At(c, third * size[c] / 6f, a, side * (size[a] * 0.5f + rodCentre), t, 0f), a, c, t, RodRadius, RodLength);
            }
            parts.Box(At(a, 0f, c, side * (size[c] * 0.5f + outerCentre), t, 0f), At(a, size[a] + 2f * bandOut, c, OuterWidth, t, depth));
            parts.Box(At(c, 0f, a, side * (size[a] * 0.5f + outerCentre), t, 0f), At(c, size[c] + 2f * bandOut, a, OuterWidth, t, depth));
            for (int other = -1; other <= 1; other += 2)
                parts.Box(At(a, side * (size[a] * 0.5f + outerCentre), c, other * (size[c] * 0.5f + outerCentre), t, 0f), At(a, OuterWidth, c, OuterWidth, t, depth));
        }
    }

    // A vector from three (axis, value) pairs
    private static Vector3 At(int i, float x, int j, float y, int k, float z) {
        Vector3 v = Vector3.zero;
        v[i] = x;
        v[j] = y;
        v[k] = z;
        return v;
    }

    // Flat-shaded shapes in metres, each face with its own vertices
    private sealed class Parts {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<int> triangles = new List<int>();

        // A face from its corners in order, facing along n
        internal void Face(Vector3[] corners, Vector3 n) {
            int first = vertices.Count;
            foreach (Vector3 p in corners) { vertices.Add(p); normals.Add(n); }
            for (int k = 1; k + 1 < corners.Length; k++) {
                // Unity's front face of (p0, p1, p2) faces along Cross(p1 - p0, p2 - p0)
                bool outward = Vector3.Dot(Vector3.Cross(corners[k] - corners[0], corners[k + 1] - corners[0]), n) > 0f;
                triangles.Add(first);
                triangles.Add(first + (outward ? k : k + 1));
                triangles.Add(first + (outward ? k + 1 : k));
            }
        }

        internal void Box(Vector3 centre, Vector3 size) {
            Vector3 h = size * 0.5f;
            for (int axis = 0; axis < 3; axis++) {
                int u = (axis + 1) % 3, v = (axis + 2) % 3;
                for (int sign = -1; sign <= 1; sign += 2) {
                    var corners = new Vector3[4];
                    for (int k = 0; k < 4; k++)
                        corners[k] = centre + At(axis, sign * h[axis], u, (k == 1 || k == 2 ? 1 : -1) * h[u], v, (k >= 2 ? 1 : -1) * h[v]);
                    Face(corners, At(axis, sign, u, 0f, v, 0f));
                }
            }
        }

        // A hexagonal rod along axis 'along', with two corners pointing along 'through'
        internal void Rod(Vector3 centre, int along, int across, int through, float radius, float length) {
            var ring = new Vector3[6];
            for (int k = 0; k < 6; k++) {
                float angle = Mathf.PI * 0.5f + k * Mathf.PI / 3f;
                ring[k] = At(across, radius * Mathf.Cos(angle), through, radius * Mathf.Sin(angle), along, 0f);
            }
            Vector3 end = At(along, length * 0.5f, across, 0f, through, 0f);
            for (int k = 0; k < 6; k++) {
                Vector3 p = ring[k], q = ring[(k + 1) % 6];
                Face(new[] { centre + p - end, centre + q - end, centre + q + end, centre + p + end }, (p + q).normalized);
            }
            var near = new Vector3[6];
            var far = new Vector3[6];
            for (int k = 0; k < 6; k++) { near[k] = centre + ring[k] - end; far[k] = centre + ring[k] + end; }
            Face(near, -end.normalized);
            Face(far, end.normalized);
        }

        // In the pane mesh's own units: positions divided by the scale. A normal n in metres becomes
        // n * scale, so that the pane's scale turns it back into n.
        internal Mesh ToMesh(Vector3 origin, Vector3 scale) {
            var v = new Vector3[vertices.Count];
            var n = new Vector3[normals.Count];
            for (int i = 0; i < v.Length; i++) {
                v[i] = origin + new Vector3(vertices[i].x / scale.x, vertices[i].y / scale.y, vertices[i].z / scale.z);
                n[i] = Vector3.Scale(normals[i], scale).normalized;
            }
            var mesh = new Mesh { name = "PuzzleGlass frame" };
            mesh.vertices = v;
            mesh.normals = n;
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}

} // namespace BigPuzzleSDK
