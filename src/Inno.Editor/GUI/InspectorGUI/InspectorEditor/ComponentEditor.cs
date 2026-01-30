using System;
using System.Linq;
using Inno.Core.ECS;
using Inno.Core.Serialization;
using Inno.Editor.GUI.PropertyGUI;

namespace Inno.Editor.GUI.InspectorGUI.InspectorEditor;

/// <summary>
/// Default Render Components' Serialized Properties
/// </summary>
[InspectorEditorGUI(typeof(GameComponent))]
public class ComponentEditor : IInspectorEditor
{
    public virtual void OnInspectorGUI(object target)
    {
        if (target is not GameComponent comp) { return; }
        
        string compName = comp.GetType().Name;
        Action onClose = comp is Transform ? () => { } : () => comp.gameObject.RemoveComponent(comp);
        
        if (EditorGuiLayout.CollapsingHeader(compName, onClose))
        {
            var serializedProps = ((ISerializable)comp).GetSerializedProperties();
            if (serializedProps.Count == 0)
            {
                EditorGuiLayout.Label("No editable properties.");
            }
            
            foreach (var prop in serializedProps)
            {
                if (PropertyRendererRegistry.TryGetRenderer(prop.propertyType, out var renderer))
                {
                    renderer!.Bind(prop.name, () => prop.GetValue(), val => prop.SetValue(val), (prop.visibility & PropertyVisibility.Readonly) != 0);
                }
                else
                {
                    EditorGuiLayout.Label($"No renderer for {prop.name} ({prop.propertyType.Name})");
                }
            }
        }
    }
}