using Inno.Core.Utility;

namespace Inno.Editor.GUI.PropertyGUI;

public static class PropertyRendererRegistry
{
    private static readonly Dictionary<Type, IPropertyRenderer> RENDERERS = new();
    private static readonly Dictionary<Type, Type> OPEN_GENERIC_RENDERERS = new();

    [TypeCacheRefresh]
    private static void ReloadAll()
    {
        RENDERERS.Clear();
        OPEN_GENERIC_RENDERERS.Clear();
        
        foreach (var type in TypeCacheManager.GetTypesImplementing<IPropertyRenderer>())
        {
            Register(type);
        }
    }

    private static void Register(Type type)
    {
        if (type.IsAbstract || type.IsInterface)
            return;

        var baseType = type.BaseType;
        while (baseType != null)
        {
            if (baseType.IsGenericType && baseType.GetGenericTypeDefinition() == typeof(PropertyRenderer<>))
            {
                if (type.IsGenericTypeDefinition)
                {
                    var genericArg = baseType.GetGenericArguments()[0];
                    var genericParam = genericArg.IsGenericType ? genericArg.GetGenericTypeDefinition() : genericArg;
                    OPEN_GENERIC_RENDERERS[genericParam] = type;
                }
                else
                {
                    if (Activator.CreateInstance(type) is IPropertyRenderer instance)
                    {
                        var genericArg = baseType.GetGenericArguments()[0];
                        RENDERERS[genericArg] = instance;
                    }
                }
                break;
            }
            baseType = baseType.BaseType;
        }
    }

    /// <summary>
    /// Gets the renderer for the specified type.
    /// If the renderer is not found, it will return false and the propertyRenderer will be null.
    /// If the type is a generic type, it will try to find an open generic.
    /// </summary>
    public static bool TryGetRenderer(Type type, out IPropertyRenderer? propertyRenderer)
    {
        if (RENDERERS.TryGetValue(type, out var renderer))
        {
            propertyRenderer = renderer;
            return true;
        }

        if (type.IsGenericType)
        {
            var genericDef = type.GetGenericTypeDefinition();

            if (OPEN_GENERIC_RENDERERS.TryGetValue(genericDef, out var openGenericRendererType))
            {
                var genericArgs = type.GetGenericArguments();
                var closedRendererType = openGenericRendererType.MakeGenericType(genericArgs);
                
                var instance = (IPropertyRenderer?)Activator.CreateInstance(closedRendererType);
                if (instance == null)
                {
                    propertyRenderer = null;
                    return false;
                }

                RENDERERS[type] = instance;
                propertyRenderer = instance;
                return true;
            }
        }

        propertyRenderer = null;
        return false;
    }
}