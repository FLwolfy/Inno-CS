using System.Linq;
using Inno.Core.ECS;
using Inno.Core.Utility;

namespace Inno.Editor.GUI.InspectorGUI.InspectorEditor;

[InspectorEditorGUI(typeof(GameObject))]
public class GameObjectEditor : IInspectorEditor
{
    public void OnInspectorGUI(object target)
    {
        if (target is not GameObject gameObject) { return; }
        
        // Render Components
        EditorGUILayout.BeginScope(gameObject.id.GetHashCode());
        OnShowComponents(gameObject);
        OnShowAddComponent(gameObject);
        EditorGUILayout.EndScope();
    }
    

    private static void OnShowComponents(GameObject gameObject)
    {
        var components = gameObject.GetAllComponents();
        foreach (var comp in components)
        {
            if (InspectorEditorRegistry.TryGetEditor(comp.GetType(), out var editor))
            {
                editor!.OnInspectorGUI(comp);
            }
            else if (InspectorEditorRegistry.TryGetEditor(typeof(GameComponent), out var defaultEditor))
            {
                defaultEditor!.OnInspectorGUI(comp);
            }
            
            EditorGUILayout.Space(10f);
        }
    }

    private static void OnShowAddComponent(GameObject gameObject)
    {
        var existingTypes = gameObject.GetAllComponents()
            .Select(c => c.GetType())
            .ToHashSet();
        
        var componentTypes = TypeCacheManager.GetSubTypesOf<GameComponent>()
            .Where(t => !t.IsAbstract && !t.IsInterface && !existingTypes.Contains(t))
            .ToArray();
        
        var typeNames = componentTypes.Select(t => t.Name).ToArray();

        EditorGUILayout.BeginAlignment(EditorGUILayout.LayoutAlign.Center);

        if (EditorGUILayout.PopupMenu("Add Component", "No available components to add.", typeNames, out var selectedIndex))
        {
            var selectedType = componentTypes[selectedIndex!.Value];
            gameObject.AddComponent(selectedType);
        }

        EditorGUILayout.EndAlignment();
    }
}