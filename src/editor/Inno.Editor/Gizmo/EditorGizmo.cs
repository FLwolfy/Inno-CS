namespace Inno.Editor.Gizmo;

public abstract class EditorGizmo
{
    /// <summary>
    /// The visibility of the gizmo.
    /// </summary>
    public bool isVisible = true;
    
    /// <summary>
    /// This method is called to draw the gizmo in the ImGui context.
    /// It should be overridden by derived classes to implement the specific drawing logic.
    /// </summary>
    internal abstract void Draw();
}