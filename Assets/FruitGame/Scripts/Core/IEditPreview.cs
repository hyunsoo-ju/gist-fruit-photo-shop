namespace FruitGame
{
    /// <summary>
    /// A part of the title screen that's built at runtime and that the Editor also shows before Play
    /// (Editor/TitlePreview.cs; Docs/DESIGN.md §16). BuildPreview runs the component's own building code in edit mode;
    /// EndPreview undoes what it changed on objects that are saved with the scene (colours, materials, static events,
    /// references). The Editor deletes the objects the preview made, and the meshes, materials and textures they use,
    /// before every save, before Play and before scripts reload, so none of it is ever saved.
    /// </summary>
    public interface IEditPreview
    {
        /// <summary>Lower builds first (the voxel button renderer before the buttons).</summary>
        int PreviewOrder { get; }
        void BuildPreview();
        void EndPreview();
    }
}
