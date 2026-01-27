using System.Linq;
using Inno.Core.ECS;
using Inno.Core.Serialization;
using Inno.Editor.GUI.PropertyGUI;

namespace Inno.Editor.GUI.InspectorGUI.InspectorEditor;

[InspectorEditorGUI(typeof(Transform))]
public class TransformEditor : ComponentEditor
{
    public override void OnInspectorGUI(object target)
    {
        if (target is not Transform comp) { return; }
        
        string compName = comp.GetType().Name;
        
        if (EditorGuiLayout.CollapsingHeader(compName))
        {
            var serializedProps = ((ISerializable)comp).GetSerializedProperties().Where(p => p.visibility != SerializedProperty.PropertyVisibility.Hide).ToList();
            
            foreach (var prop in serializedProps)
            {
                if (prop.visibility != SerializedProperty.PropertyVisibility.Show)
                    continue;
                
                if (PropertyRendererRegistry.TryGetRenderer(prop.propertyType, out var renderer))
                {
                    renderer!.Bind(prop.name, () => prop.GetValue(), val => prop.SetValue(val), prop.visibility == SerializedProperty.PropertyVisibility.Show);
                }
            }
        }
    }
}