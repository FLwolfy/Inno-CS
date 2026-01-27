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
        EditorGuiLayout.BeginScope(gameObject.id.GetHashCode());
        OnShowComponents(gameObject);
        OnShowAddComponent(gameObject);
        EditorGuiLayout.EndScope();
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
            
            EditorGuiLayout.Space(10f);
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

        EditorGuiLayout.BeginAlignment(EditorGuiLayout.LayoutAlign.Center);

        if (EditorGuiLayout.PopupMenu("Add Component", "No available components to add.", typeNames, out var selectedIndex))
        {
            var selectedType = componentTypes[selectedIndex!.Value];
            gameObject.AddComponent(selectedType);
        }

        EditorGuiLayout.EndAlignment();
    }
}