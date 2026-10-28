using UnityEngine;

namespace FruitGame
{
    public enum VoxelLayer
    {
        World,
        Far
    }

    /// <summary>
    /// Marks a scene mesh as voxelised (Docs/DESIGN.md §12): remembers the original smooth mesh so the voxel baker
    /// (FruitGame > Voxels) can rebuild the block version at this object's scale whenever block sizes change.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    public class VoxelSource : MonoBehaviour
    {
        [Tooltip("The smooth mesh the blocks are built from.")]
        public Mesh original;
        [Tooltip("Which GameConfig block size to use.")]
        public VoxelLayer layer = VoxelLayer.World;
        [Tooltip("Overrides the GameConfig block size when above 0 (metres).")]
        public float blockSizeOverride;
    }
}
