using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FruitGame
{
    /// <summary>
    /// Builds a block mesh from a 3D grid of colours (Docs/DESIGN.md §11, the voxel title and icon). Only faces next to
    /// empty cells are emitted, vertices are welded per grid corner, face direction and colour (no seams), and colours
    /// are stored as vertex colours so one material draws every block.
    /// </summary>
    public static class BlockMesh
    {
        static readonly Vector3Int[] Dirs =
        {
            new Vector3Int(1, 0, 0), new Vector3Int(-1, 0, 0),
            new Vector3Int(0, 1, 0), new Vector3Int(0, -1, 0),
            new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1),
        };

        static readonly Vector3Int[][] FaceCorners = BuildFaceCorners();

        /// <param name="color">Colour of the block at (x, y, z); alpha 0 = empty.</param>
        /// <param name="size">Grid dimensions.</param>
        /// <param name="block">Block size in local units.</param>
        /// <param name="origin">Local position of the grid's (0, 0, 0) corner.</param>
        public static Mesh Build(System.Func<int, int, int, Color32> color, Vector3Int size, float block, Vector3 origin, string name)
        {
            bool Solid(int x, int y, int z) =>
                x >= 0 && y >= 0 && z >= 0 && x < size.x && y < size.y && z < size.z && color(x, y, z).a > 0;

            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var colors = new List<Color32>();
            var tris = new List<int>();
            var welded = new Dictionary<(int, int, int, int, int), int>();

            int Vertex(Vector3Int c, int dir, Color32 col)
            {
                int key = col.r | (col.g << 8) | (col.b << 16);
                var k = (c.x, c.y, c.z, dir, key);
                if (welded.TryGetValue(k, out int v)) return v;
                v = positions.Count;
                positions.Add(origin + (Vector3)c * block);
                normals.Add(Dirs[dir]);
                colors.Add(col);
                welded[k] = v;
                return v;
            }

            for (int z = 0; z < size.z; z++)
            for (int y = 0; y < size.y; y++)
            for (int x = 0; x < size.x; x++)
            {
                var col = color(x, y, z);
                if (col.a == 0) continue;
                var cell = new Vector3Int(x, y, z);
                for (int dir = 0; dir < 6; dir++)
                {
                    var d = Dirs[dir];
                    if (Solid(x + d.x, y + d.y, z + d.z)) continue;
                    var c = FaceCorners[dir];
                    int v0 = Vertex(cell + c[0], dir, col), v1 = Vertex(cell + c[1], dir, col);
                    int v2 = Vertex(cell + c[2], dir, col), v3 = Vertex(cell + c[3], dir, col);
                    tris.Add(v0); tris.Add(v1); tris.Add(v2);
                    tris.Add(v0); tris.Add(v2); tris.Add(v3);
                }
            }

            var mesh = new Mesh { name = name };
            if (positions.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Extrudes a 2D pixel shape in the sticker style used by the title and the HUD digits: the shape's own blocks
        /// (depth deep, colour from faceAt), a one-block outline ring set back one block, and a shadow block at the back,
        /// offset one block down-right. The grid is padded by 1 for the ring and by 1 right/below for the shadow.
        /// </summary>
        public static Color32[,,] Sticker(System.Func<int, int, bool> filled, System.Func<int, int, Color32> faceAt,
            int w, int h, int depth, Color32 ring, Color32 shadow, out Vector3Int size)
        {
            size = new Vector3Int(w + 3, h + 3, depth);
            var cells = new Color32[size.x, size.y, size.z];
            bool Filled(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && filled(x, y);
            bool Grown(int gx, int gy)
            {
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (Filled(gx - 1 + dx, gy - 2 + dy)) return true;
                return false;
            }
            for (int gy = 0; gy < size.y; gy++)
            for (int gx = 0; gx < size.x; gx++)
            {
                bool letter = Filled(gx - 1, gy - 2);
                bool isRing = !letter && Grown(gx, gy);
                bool shade = !letter && !isRing && Grown(gx - 1, gy + 1);
                for (int z = 0; z < depth; z++)
                {
                    if (letter) cells[gx, gy, z] = faceAt(gx - 1, gy - 2);
                    else if (isRing && z >= 1) cells[gx, gy, z] = ring;
                    else if (shade && z >= depth - 1) cells[gx, gy, z] = shadow;
                }
            }
            return cells;
        }

        /// <summary>The four corners of each face, wound clockwise as seen from outside (Unity's front face).</summary>
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
    }
}
