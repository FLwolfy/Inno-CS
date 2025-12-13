using System.Reflection;

namespace Inno.Core.Utility;

[AttributeUsage(AttributeTargets.Method)]
public sealed class TypeCacheRefreshAttribute : Attribute;

public static class TypeCacheManager
{
    private const string C_INNO_NAMESPACE = "Inno";
    
    private static readonly Dictionary<Type, List<Type>> SUBCLASS_CACHE = new();
    private static readonly Dictionary<Type, List<Type>> INTERFACE_CACHE = new();
    private static readonly Dictionary<Type, List<Type>> ATTRIBUTE_CACHE = new();

    private static bool m_isDirty = true;
    private static event Action? OnRefreshed;

    public static void Initialize()
    {
        SubscribeRefreshHooks();
            
        AppDomain.CurrentDomain.AssemblyLoad += (_, __) =>
        {
            m_isDirty = true;
        };
        
        Refresh();
    }
    
    private static void SubscribeRefreshHooks()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic);

        var allTypes = assemblies
            .SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch { return Type.EmptyTypes; }
            })
            .Where(t => t.Namespace?.StartsWith(C_INNO_NAMESPACE) ?? false)
            .ToArray();
        
        foreach (var type in allTypes)
        {
            foreach (var method in type.GetMethods(
                         BindingFlags.Static |
                         BindingFlags.Public |
                         BindingFlags.NonPublic))
            {
                if (!method.IsDefined(typeof(TypeCacheRefreshAttribute), false))
                    continue;

                if (method.ReturnType != typeof(void) ||
                    method.GetParameters().Length != 0)
                {
                    throw new InvalidOperationException(
                        $"[TypeCacheRefresh] method must be 'static void Method()': " +
                        $"{type.FullName}.{method.Name}");
                }

                OnRefreshed += () => method.Invoke(null, null);
            }
        }
    }
    
    /// <summary>
    /// This needs to be called when a new type is registered to the cache.
    /// </summary>
    public static void Refresh()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic);

        var allTypes = assemblies
            .SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch { return Type.EmptyTypes; }
            })
            .Where(t => !t.IsAbstract && !t.IsInterface && (t.Namespace?.StartsWith(C_INNO_NAMESPACE) ?? false))
            .ToArray();

        SUBCLASS_CACHE.Clear();
        INTERFACE_CACHE.Clear();
        ATTRIBUTE_CACHE.Clear();

        foreach (var type in allTypes)
        {
            if (type.IsAbstract) continue;

            // Index by base type
            var baseType = type.BaseType;
            while (baseType != null && baseType != typeof(object))
            {
                if (!SUBCLASS_CACHE.TryGetValue(baseType, out var list))
                    SUBCLASS_CACHE[baseType] = list = new();
                list.Add(type);
                baseType = baseType.BaseType;
            }

            // Index by interfaces
            foreach (var iface in type.GetInterfaces())
            {
                if (!INTERFACE_CACHE.TryGetValue(iface, out var list))
                    INTERFACE_CACHE[iface] = list = new();
                list.Add(type);
            }

            // Index by attributes
            foreach (var attr in type.GetCustomAttributes(inherit: true))
            {
                var attrType = attr.GetType();
                if (!ATTRIBUTE_CACHE.TryGetValue(attrType, out var list))
                    ATTRIBUTE_CACHE[attrType] = list = new();
                list.Add(type);
            }
        }

        m_isDirty = false;
        OnRefreshed?.Invoke();
    }

    /// <summary>
    /// Gets all subtypes of the given type T in the Assembly Company specified above.
    /// </summary>
    public static IReadOnlyList<Type> GetSubTypesOf<T>()
    {
        if (m_isDirty) Refresh();
        if (SUBCLASS_CACHE.TryGetValue(typeof(T), out var list)) return list;
        return [];
    }

    /// <summary>
    /// Gets all subtypes of the given interface in the Assembly Company specified above.
    /// </summary>
    public static IReadOnlyList<Type> GetTypesImplementing<TInterface>()
    {
        if (m_isDirty) Refresh();
        if (INTERFACE_CACHE.TryGetValue(typeof(TInterface), out var list)) return list;
        return [];
    }

    /// <summary>
    /// Gets all types with the specified attribute in the Assembly Namespace specified above.
    /// </summary>
    public static IReadOnlyList<Type> GetTypesWithAttribute<TAttr>() where TAttr : Attribute
    {
        if (m_isDirty) Refresh();
        if (ATTRIBUTE_CACHE.TryGetValue(typeof(TAttr), out var list)) return list;
        return [];
    }
}
