using System;

namespace Inno.Editor.GUI.InspectorGUI;

[AttributeUsage(AttributeTargets.Class)]
public class InspectorEditorGUIAttribute : Attribute
{
    /// <summary>
    /// The type that this editor is responsible for rendering in the inspector.
    /// </summary>
    public readonly Type targetType;
    
    /// <summary>
    /// This attribute is used to mark a class as an inspector editor for a specific type.
    /// </summary>
    public InspectorEditorGUIAttribute(Type type) { targetType = type; }
}
