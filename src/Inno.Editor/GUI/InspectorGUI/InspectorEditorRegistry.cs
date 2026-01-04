using System;
using System.Collections.Generic;
using System.Reflection;
using Inno.Core.Utility;

namespace Inno.Editor.GUI.InspectorGUI;

public static class InspectorEditorRegistry
{
    private static readonly Dictionary<Type, IInspectorEditor> REGISTRY = new();

    [TypeCacheRefresh]
    private static void ReloadAll()
    {
        REGISTRY.Clear();
        
        foreach (var editorType in TypeCacheManager.GetTypesWithAttribute<InspectorEditorGUIAttribute>())
        {
            Register(editorType);
        }
    }

    private static void Register(Type type)
    {
        if (Activator.CreateInstance(type) is IInspectorEditor editor)
        {
            var attr = type.GetCustomAttribute<InspectorEditorGUIAttribute>();
            REGISTRY[attr!.targetType] = editor;
        }
    }
    
    /// <summary>
    /// Get the editor for the specified type.
    /// If the editor is not found, it will return false and the editor will be null
    /// </summary>
    public static bool TryGetEditor(Type type, out IInspectorEditor? editor)
        => REGISTRY.TryGetValue(type, out editor);
}
