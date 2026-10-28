using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FruitGame.EditorTools
{
    /// <summary>
    /// Rebuilds triangle meshes out of blocks (Docs/DESIGN.md §12). Every grid cell a triangle passes through becomes a
    /// block, the enclosed inside is filled, and only faces that touch outside air are emitted. Vertices are welded on
    /// the integer grid (shared per corner, face direction and colour), so neighbouring faces meet exactly: no T-junctions,
    /// no hairline seams.
    ///
    /// Two colour modes:
    /// - <b>Material</b>: each block keeps the material (submesh) of the nearest triangle. For flat-coloured models.
    /// - <b>Palette</b>: each block averages the texture colour of the surface under it; the colours are then reduced to
    ///   a few per model with k-means, so blocks get clean flat colours instead of stripes from a gradient atlas.
    /// </summary>
    public static class VoxelMesher
    {
        public class Source
        {
            public readonly List<Vector3> positions = new List<Vector3>();
            public readonly List<Vector2> uvs = new List<Vector2>();
            /// <summary>Triangles as vertex index triples, each with its material slot.</summary>
            public readonly List<(int a, int b, int c, int slot)> triangles = new List<(int, int, int, int)>();
            public int slotCount = 1;
            /// <summary>Palette mode: colour lookup by uv (and slot). Null = Material mode.</summary>
            public System.Func<Vector2, int, Color> sampleColor;
        }

        public class Result
        {
            public Mesh mesh;
            /// <summary>Palette mode: the reduced colours; mesh UVs index them via the paletteUv callback.</summary>
            public Color[] colors;
            public int blocks, faces;
        }

        /// <summary>
        /// Adds a mesh to a source, transformed into the source's space. slotOffset shifts its submesh indices.
        /// </summary>
        public static void Add(Source src, Mesh mesh, Matrix4x4 toSource, int slotOffset = 0)
        {
            int baseIndex = src.positions.Count;
            var verts = mesh.vertices;
            var uv = mesh.uv;
            for (int i = 0; i < verts.Length; i++)
            {
                src.positions.Add(toSource.MultiplyPoint3x4(verts[i]));
                src.uvs.Add(uv != null && uv.Length == verts.Length ? uv[i] : Vector2.zero);
            }
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                if (mesh.GetTopology(s) != MeshTopology.Triangles) continue;
                var t = mesh.GetTriangles(s);
                for (int k = 0; k < t.Length; k += 3)
                    src.triangles.Add((baseIndex + t[k], baseIndex + t[k + 1], baseIndex + t[k + 2], slotOffset + s));
            }
            src.slotCount = Mathf.Max(src.slotCount, slotOffset + mesh.subMeshCount);
        }

        /// <param name="cell">Block size per axis, in source units.</param>
        /// <param name="paletteSize">Palette mode: how many colours to reduce to.</param>
        /// <param name="paletteUv">Palette mode: uv of colour i in the final palette texture.</param>
        public static Result Build(Source src, Vector3 cell, string name, int paletteSize = 5, System.Func<int, Vector2> paletteUv = null)
        {
            bool palette = src.sampleColor != null;
            var min = src.positions[0];
            var max = src.positions[0];
            foreach (var p in src.positions) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
            // Grid aligned to the source origin, with an empty border so the outside flood fill can reach everywhere.
            var origin = new Vector3(
                Mathf.Floor(min.x / cell.x) * cell.x - cell.x,
                Mathf.Floor(min.y / cell.y) * cell.y - cell.y,
                Mathf.Floor(min.z / cell.z) * cell.z - cell.z);
            int nx = Mathf.CeilToInt((max.x - origin.x) / cell.x) + 2;
            int ny = Mathf.CeilToInt((max.y - origin.y) / cell.y) + 2;
            int nz = Mathf.CeilToInt((max.z - origin.z) / cell.z) + 2;
            int count = nx * ny * nz;
            int Index(int x, int y, int z) => (z * ny + y) * nx + x;
            var inv = new Vector3(1f / cell.x, 1f / cell.y, 1f / cell.z);
            Vector3 ToCell(Vector3 p) => Vector3.Scale(p - origin, inv);

            var best = new float[count];
            for (int i = 0; i < count; i++) best[i] = float.MaxValue;
            var slot = new int[count];
            var colorSum = palette ? new Color[count] : null;
            var weightSum = palette ? new float[count] : null;

            foreach (var (ia, ib, ic, s) in src.triangles)
            {
                var a = ToCell(src.positions[ia]);
                var b = ToCell(src.positions[ib]);
                var c = ToCell(src.positions[ic]);
                if (Vector3.Cross(b - a, c - a).sqrMagnitude < 1e-12f) continue;
                var lo = Vector3.Min(a, Vector3.Min(b, c));
                var hi = Vector3.Max(a, Vector3.Max(b, c));
                int x0 = Mathf.Max(0, Mathf.FloorToInt(lo.x)), x1 = Mathf.Min(nx - 1, Mathf.FloorToInt(hi.x));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(lo.y)), y1 = Mathf.Min(ny - 1, Mathf.FloorToInt(hi.y));
                int z0 = Mathf.Max(0, Mathf.FloorToInt(lo.z)), z1 = Mathf.Min(nz - 1, Mathf.FloorToInt(hi.z));
                for (int z = z0; z <= z1; z++)
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var centre = new Vector3(x + 0.5f, y + 0.5f, z + 0.5f);
                    if (!TriangleOverlapsBox(centre, 0.5f, a, b, c)) continue;
                    var p = ClosestPointOnTriangle(centre, a, b, c, out var w);
                    float d = (p - centre).sqrMagnitude;
                    int i = Index(x, y, z);
                    if (palette)
                    {
                        var uv = src.uvs[ia] * w.x + src.uvs[ib] * w.y + src.uvs[ic] * w.z;
                        float weight = 1f / (Mathf.Sqrt(d) + 0.1f);
                        colorSum[i] += src.sampleColor(uv, s) * weight;
                        weightSum[i] += weight;
                    }
                    if (d >= best[i]) continue;
                    best[i] = d;
                    slot[i] = s;
                }
            }

            // Outside air: everything empty that the grid border can reach. Empty cells it can't reach are inside.
            var outside = new bool[count];
            var queue = new Queue<int>();
            for (int z = 0; z < nz; z++)
            for (int y = 0; y < ny; y++)
            for (int x = 0; x < nx; x++)
            {
                if (x != 0 && y != 0 && z != 0 && x != nx - 1 && y != ny - 1 && z != nz - 1) continue;
                int i = Index(x, y, z);
                if (best[i] != float.MaxValue || outside[i]) continue;
                outside[i] = true;
                queue.Enqueue(i);
            }
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                int x = i % nx, y = (i / nx) % ny, z = i / (nx * ny);
                foreach (var d in Dirs)
                {
                    int ax = x + d.x, ay = y + d.y, az = z + d.z;
                    if (ax < 0 || ay < 0 || az < 0 || ax >= nx || ay >= ny || az >= nz) continue;
                    int j = Index(ax, ay, az);
                    if (outside[j] || best[j] != float.MaxValue) continue;
                    outside[j] = true;
                    queue.Enqueue(j);
                }
            }

            bool Air(int x, int y, int z) =>
                x < 0 || y < 0 || z < 0 || x >= nx || y >= ny || z >= nz || outside[Index(x, y, z)];

            // Visible blocks: surface blocks with at least one face to the outside.
            var visible = new List<int>();
            for (int i = 0; i < count; i++)
            {
                if (best[i] == float.MaxValue) continue;
                int x = i % nx, y = (i / nx) % ny, z = i / (nx * ny);
                foreach (var d in Dirs)
                    if (Air(x + d.x, y + d.y, z + d.z)) { visible.Add(i); break; }
            }

            // Palette mode: reduce the visible blocks' averaged colours to a few clean ones.
            Color[] centres = null;
            int[] colorOf = null;
            if (palette)
            {
                var colors = new Color[visible.Count];
                for (int k = 0; k < visible.Count; k++)
                {
                    int i = visible[k];
                    colors[k] = weightSum[i] > 0f ? colorSum[i] / weightSum[i] : Color.magenta;
                }
                centres = KMeans(colors, Mathf.Min(paletteSize, Mathf.Max(1, colors.Length)), out var assign);
                colorOf = new int[count];
                for (int k = 0; k < visible.Count; k++) colorOf[visible[k]] = assign[k];
            }

            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            int groups = palette ? 1 : src.slotCount;
            var tris = new List<int>[groups];
            for (int g = 0; g < groups; g++) tris[g] = new List<int>();
            var welded = new Dictionary<(int, int, int, int, int), int>();
            int faces = 0;

            int Vertex(int cx, int cy, int cz, int dir, int key, Vector2 uv)
            {
                var k = (cx, cy, cz, dir, key);
                if (welded.TryGetValue(k, out int v)) return v;
                v = positions.Count;
                positions.Add(origin + Vector3.Scale(new Vector3(cx, cy, cz), cell));
                normals.Add(Dirs[dir]);
                uvs.Add(uv);
                welded[k] = v;
                return v;
            }

            foreach (int i in visible)
            {
                int x = i % nx, y = (i / nx) % ny, z = i / (nx * ny);
                int key = palette ? colorOf[i] : slot[i];
                var uv = palette && paletteUv != null ? paletteUv(colorOf[i]) : Vector2.zero;
                var list = tris[palette ? 0 : slot[i]];
                for (int dir = 0; dir < 6; dir++)
                {
                    var d = Dirs[dir];
                    if (!Air(x + d.x, y + d.y, z + d.z)) continue;
                    var corners = FaceCorners[dir];
                    int v0 = Vertex(x + corners[0].x, y + corners[0].y, z + corners[0].z, dir, key, uv);
                    int v1 = Vertex(x + corners[1].x, y + corners[1].y, z + corners[1].z, dir, key, uv);
                    int v2 = Vertex(x + corners[2].x, y + corners[2].y, z + corners[2].z, dir, key, uv);
                    int v3 = Vertex(x + corners[3].x, y + corners[3].y, z + corners[3].z, dir, key, uv);
                    list.Add(v0); list.Add(v1); list.Add(v2);
                    list.Add(v0); list.Add(v2); list.Add(v3);
                    faces++;
                }
            }

            var mesh = new Mesh { name = name };
            if (positions.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = groups;
            for (int g = 0; g < groups; g++) mesh.SetTriangles(tris[g], g);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false);
            return new Result { mesh = mesh, colors = centres, blocks = visible.Count, faces = faces };
        }

        static readonly Vector3Int[] Dirs =
        {
            new Vector3Int(1, 0, 0), new Vector3Int(-1, 0, 0),
            new Vector3Int(0, 1, 0), new Vector3Int(0, -1, 0),
            new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1),
        };

        /// <summary>
        /// The four grid corners (offsets from the block's min corner) of each face, wound clockwise as seen from
        /// outside, which is Unity's front face.
        /// </summary>
        static readonly Vector3Int[][] FaceCorners = BuildFaceCorners();

        static Vector3Int[][] BuildFaceCorners()
        {
            var result = new Vector3Int[6][];
            for (int dir = 0; dir < 6; dir++)
            {
                var n = (Vector3)Dirs[dir];
                Vector3 u, v;
                if (Dirs[dir].x != 0) { u = Vector3.forward; v = Vector3.up; }
                else if (Dirs[dir].y != 0) { u = Vector3.right; v = Vector3.forward; }
                else { u = Vector3.up; v = Vector3.right; }
                if (Vector3.Dot(Vector3.Cross(u, v), n) > 0f) (u, v) = (v, u);
                var centre = new Vector3(0.5f, 0.5f, 0.5f) + n * 0.5f;
                var corners = new[] { -u - v, -u + v, u + v, u - v };
                result[dir] = new Vector3Int[4];
                for (int k = 0; k < 4; k++) result[dir][k] = Vector3Int.RoundToInt(centre + corners[k] * 0.5f);
            }
            return result;
        }

        /// <summary>k-means in a lightness-weighted RGB space, seeded deterministically (k-means++ with a fixed seed).</summary>
        static Color[] KMeans(Color[] colors, int k, out int[] assign)
        {
            assign = new int[colors.Length];
            if (colors.Length == 0) return new Color[0];
            var rng = new System.Random(1234);
            var centres = new List<Vector3> { V(colors[rng.Next(colors.Length)]) };
            var dist = new float[colors.Length];
            while (centres.Count < k)
            {
                float total = 0f;
                for (int i = 0; i < colors.Length; i++)
                {
                    float m = float.MaxValue;
                    foreach (var c in centres) m = Mathf.Min(m, (V(colors[i]) - c).sqrMagnitude);
                    dist[i] = m;
                    total += m;
                }
                if (total <= 1e-9f) break;
                double r = rng.NextDouble() * total;
                int pick = 0;
                for (; pick < colors.Length - 1; pick++) { r -= dist[pick]; if (r <= 0) break; }
                centres.Add(V(colors[pick]));
            }
            for (int iter = 0; iter < 20; iter++)
            {
                var sum = new Vector3[centres.Count];
                var n = new int[centres.Count];
                for (int i = 0; i < colors.Length; i++)
                {
                    var p = V(colors[i]);
                    int bestK = 0;
                    float bestD = float.MaxValue;
                    for (int c = 0; c < centres.Count; c++)
                    {
                        float d = (p - centres[c]).sqrMagnitude;
                        if (d < bestD) { bestD = d; bestK = c; }
                    }
                    assign[i] = bestK;
                    sum[bestK] += p;
                    n[bestK]++;
                }
                for (int c = 0; c < centres.Count; c++) if (n[c] > 0) centres[c] = sum[c] / n[c];
            }
            var result = new Color[centres.Count];
            for (int c = 0; c < centres.Count; c++) result[c] = new Color(centres[c].x, centres[c].y, centres[c].z, 1f);
            return result;
        }

        static Vector3 V(Color c) => new Vector3(c.r, c.g, c.b);

        /// <summary>Separating-axis test of a triangle against an axis-aligned cube (centre, half size h).</summary>
        static bool TriangleOverlapsBox(Vector3 centre, float h, Vector3 a, Vector3 b, Vector3 c)
        {
            h *= 1.0001f;
            var v0 = a - centre;
            var v1 = b - centre;
            var v2 = c - centre;
            if (Mathf.Max(v0.x, Mathf.Max(v1.x, v2.x)) < -h || Mathf.Min(v0.x, Mathf.Min(v1.x, v2.x)) > h) return false;
            if (Mathf.Max(v0.y, Mathf.Max(v1.y, v2.y)) < -h || Mathf.Min(v0.y, Mathf.Min(v1.y, v2.y)) > h) return false;
            if (Mathf.Max(v0.z, Mathf.Max(v1.z, v2.z)) < -h || Mathf.Min(v0.z, Mathf.Min(v1.z, v2.z)) > h) return false;

            var n = Vector3.Cross(v1 - v0, v2 - v1);
            float r = h * (Mathf.Abs(n.x) + Mathf.Abs(n.y) + Mathf.Abs(n.z));
            if (Mathf.Abs(Vector3.Dot(n, v0)) > r) return false;

            var e0 = v1 - v0; var e1 = v2 - v1; var e2 = v0 - v2;
            return Axis(e0, Vector3.right) && Axis(e0, Vector3.up) && Axis(e0, Vector3.forward)
                && Axis(e1, Vector3.right) && Axis(e1, Vector3.up) && Axis(e1, Vector3.forward)
                && Axis(e2, Vector3.right) && Axis(e2, Vector3.up) && Axis(e2, Vector3.forward);

            bool Axis(Vector3 e, Vector3 ax)
            {
                var l = Vector3.Cross(e, ax);
                float p0 = Vector3.Dot(l, v0), p1 = Vector3.Dot(l, v1), p2 = Vector3.Dot(l, v2);
                float rr = h * (Mathf.Abs(l.x) + Mathf.Abs(l.y) + Mathf.Abs(l.z));
                return !(Mathf.Min(p0, Mathf.Min(p1, p2)) > rr || Mathf.Max(p0, Mathf.Max(p1, p2)) < -rr);
            }
        }

        /// <summary>Closest point on triangle abc to p (Ericson, Real-Time Collision Detection 5.1.5), with barycentric weights.</summary>
        static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c, out Vector3 w)
        {
            var ab = b - a; var ac = c - a; var ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) { w = new Vector3(1, 0, 0); return a; }
            var bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) { w = new Vector3(0, 1, 0); return b; }
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) { float t = d1 / (d1 - d3); w = new Vector3(1 - t, t, 0); return a + t * ab; }
            var cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) { w = new Vector3(0, 0, 1); return c; }
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) { float t = d2 / (d2 - d6); w = new Vector3(1 - t, 0, t); return a + t * ac; }
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
            {
                float t = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                w = new Vector3(0, 1 - t, t);
                return b + t * (c - b);
            }
            float denom = 1f / (va + vb + vc);
            float bv = vb * denom, cv = vc * denom;
            w = new Vector3(1 - bv - cv, bv, cv);
            return a + ab * bv + ac * cv;
        }
    }
}
