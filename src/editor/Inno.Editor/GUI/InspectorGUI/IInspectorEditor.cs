namespace Inno.Editor.GUI.InspectorGUI;

public interface IInspectorEditor
{
    /// <summary>
    /// This method is called to render the inspector GUI for the given target object.
    /// The target object can be any type that is registered with an InspectorEditorGUI attribute.
    /// </summary>
    void OnInspectorGUI(object target);
}