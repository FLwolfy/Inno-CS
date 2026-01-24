using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Inno.Core.Serialization;

[AttributeUsage(AttributeTargets.Method, Inherited = true)]
public sealed class OnSerializableRestored : Attribute;

/// <summary>
/// Base contract for editor/runtime state that is allowed to be saved/restored.
///
/// Allowed graph (recursive, super-nested):
/// 1) Primitive-like scalars: bool/number/decimal/string/Guid
/// 2) enum
/// 3) struct: public instance fields + public settable properties (with SerializableProperty overrides)
/// 4) ISerializable: only [SerializableProperty] members
/// 5) Containers: T[] / List / Dictionary (and compatible interfaces), where K/V/T are allowed (recursive)
///
/// Visibility rules (applies to BOTH structs and ISerializable):
/// - Show: participate in serialize + deserialize
/// - Hide: participate in neither serialize nor deserialize
/// - ReadOnly: participate in serialize only (skip deserialize); can have no setter (property) / can be readonly field
///
/// Notes:
/// - Binary serialization is provided by <see cref="SerializingState"/> (NOT by ISerializable).
/// </summary>
public interface ISerializable
{
    // =====================================================================
    //  Reflection slots for [SerializableProperty] (ISerializable objects)
    // =====================================================================

    private sealed record MemberSlot(
        string name,
        Type type,
        Func<object, object?> getter,
        Action<object, object?> setter, // may be no-op for ReadOnly
        SerializedProperty.PropertyVisibility visibility,
        int orderKey
    );

    private static readonly ConcurrentDictionary<Type, MemberSlot[]> SLOT_CACHE = new();

    public IReadOnlyList<SerializedProperty> GetSerializedProperties()
    {
        var slots = GetSlots(GetType());
        var result = new List<SerializedProperty>(slots.Length);

        foreach (var s in slots)
        {
            result.Add(new SerializedProperty(
                s.name,
                s.type,
                () => s.getter(this),
                v => s.setter(this, v),
                s.visibility
            ));
        }

        return result;
    }

    public SerializingState CaptureState()
    {
        var slots = GetSlots(GetType());
        var node = new Dictionary<string, object?>(slots.Length, StringComparer.Ordinal);

        foreach (var s in slots)
        {
            // Show + ReadOnly participate in serialization; Hide never appears in slots.
            var v = s.getter(this);
            node[s.name] = CaptureValue(v, s.type);
        }

        return new SerializingState(node);
    }

    public void RestoreState(SerializingState state)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));

        var slots = GetSlots(GetType());
        foreach (var s in slots)
        {
            // ReadOnly does NOT participate in deserialize
            if (s.visibility == SerializedProperty.PropertyVisibility.ReadOnly)
                continue;

            if (!state.values.TryGetValue(s.name, out var raw))
                continue;

            var v = RestoreValue(raw, s.type);
            s.setter(this, v);
        }

        InvokeAfterRestoreHooks();
    }

    private void InvokeAfterRestoreHooks()
    {
        const BindingFlags c_declaredFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var type = GetType();

        // base -> derived
        var chain = new List<Type>(8);
        for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            chain.Add(t);
        chain.Reverse();

        foreach (var t in chain)
        {
            foreach (var m in t.GetMethods(c_declaredFlags))
            {
                var attr = m.GetCustomAttribute<OnSerializableRestored>(inherit: true);
                if (attr == null) continue;

                if (m.GetParameters().Length != 0)
                    throw new InvalidOperationException($"{type.FullName}.{m.Name} must have 0 parameters.");
                if (m.ReturnType != typeof(void))
                    throw new InvalidOperationException($"{type.FullName}.{m.Name} must return void.");

                m.Invoke(this, null);
            }
        }
    }

    // ---------------------------------------------------------------------
    // Slot cache
    // ---------------------------------------------------------------------

    private static MemberSlot[] GetSlots(Type type)
    {
        return SLOT_CACHE.GetOrAdd(type, BuildSlots);
    }

    /// <summary>
    /// Internal helper for <see cref="SerializableGraph.ValidateAllowedTypeGraph"/>.
    /// Returns the declared member types participating in the serializable graph for this ISerializable type.
    /// </summary>
    internal static (string name, Type type)[] GetSlotsForValidation(Type type)
    {
        var slots = GetSlots(type);
        var arr = new (string name, Type type)[slots.Length];
        for (int i = 0; i < slots.Length; i++) arr[i] = (slots[i].name, slots[i].type);
        return arr;
    }

    private static MemberSlot[] BuildSlots(Type type)
    {
        const BindingFlags c_declaredFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        var chain = new List<Type>(8);
        for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            chain.Add(t);
        chain.Reverse();

        var list = new List<MemberSlot>(64);
        foreach (var t in chain)
        {
            // -----------------------------
            // Properties (declared in t)
            // -----------------------------
            foreach (var p in t.GetProperties(c_declaredFlags))
            {
                var attr = p.GetCustomAttribute<SerializablePropertyAttribute>(inherit: true);
                if (attr == null) continue;

                if (attr.propertyVisibility == SerializedProperty.PropertyVisibility.Hide)
                    continue;

                if (p.GetIndexParameters().Length != 0)
                    throw new InvalidOperationException($"{type.FullName}.{p.Name} cannot be an indexer to be [SerializableProperty].");

                if (!p.CanRead)
                    throw new InvalidOperationException($"{type.FullName}.{p.Name} must be readable to be [SerializableProperty].");

                bool isReadOnly = attr.propertyVisibility == SerializedProperty.PropertyVisibility.ReadOnly;

                // Show
                if (!isReadOnly && p.GetSetMethod(nonPublic: true) == null)
                    throw new InvalidOperationException($"{type.FullName}.{p.Name} must have setter when visibility=Show.");

                SerializableGraph.ValidateAllowedTypeGraph(p.PropertyType, $"{type.FullName}.{p.Name}");

                Func<object, object?> getter = obj => p.GetValue(obj);
                Action<object, object?> setter = BuildPropertySetter(type, p, isReadOnly);

                int depth = chain.IndexOf(t);
                int orderKey = (depth << 24) ^ p.MetadataToken;

                list.Add(new MemberSlot(
                    p.Name,
                    p.PropertyType,
                    getter,
                    setter,
                    attr.propertyVisibility,
                    orderKey));
            }

            // -----------------------------
            // Fields (declared in t)
            // -----------------------------
            foreach (var f in t.GetFields(c_declaredFlags))
            {
                var attr = f.GetCustomAttribute<SerializablePropertyAttribute>(inherit: true);
                if (attr == null) continue;

                if (attr.propertyVisibility == SerializedProperty.PropertyVisibility.Hide)
                    continue;

                bool isReadOnly = attr.propertyVisibility == SerializedProperty.PropertyVisibility.ReadOnly;

                if (!isReadOnly && f.IsInitOnly)
                    throw new InvalidOperationException($"{type.FullName}.{f.Name} is readonly; it must be writable when visibility=Show.");

                SerializableGraph.ValidateAllowedTypeGraph(f.FieldType, $"{type.FullName}.{f.Name}");

                Func<object, object?> getter = obj => f.GetValue(obj);
                Action<object, object?> setter =
                    (!isReadOnly && !f.IsInitOnly)
                        ? (obj, v) => f.SetValue(obj, v)
                        : static (_, _) => { };

                int depth = chain.IndexOf(t);
                int orderKey = (depth << 24) ^ f.MetadataToken;

                list.Add(new MemberSlot(
                    f.Name,
                    f.FieldType,
                    getter,
                    setter,
                    attr.propertyVisibility,
                    orderKey));
            }
        }

        // Handle overrides
        list = list
            .GroupBy(s => s.name, StringComparer.Ordinal)
            .Select(g => g.OrderBy(x => x.orderKey).Last())
            .ToList();

        return list.OrderBy(s => s.orderKey).ToArray();
    }

    private static Action<object, object?> BuildPropertySetter(Type ownerType, PropertyInfo p, bool isReadOnly)
    {
        if (isReadOnly)
            return static (_, _) => { };

        // allow private/protected setters
        var setMethod = p.GetSetMethod(nonPublic: true);
        if (setMethod == null)
            throw new InvalidOperationException($"{ownerType.FullName}.{p.Name} must have a setter when visibility=Show.");

        // fast path for public setter
        if (setMethod.IsPublic)
            return (obj, v) => p.SetValue(obj, v);

        // non-public setter: invoke directly
        return (obj, v) => setMethod.Invoke(obj, new[] { v });
    }

    // =====================================================================
    //  State capture/restore value conversions (in-memory graph)
    // =====================================================================

    private static object? CaptureValue(object? value, Type declaredType)
    {
        if (value == null) return null;

        var t = Nullable.GetUnderlyingType(declaredType) ?? declaredType;

        if (t.IsEnum)
            return Convert.ToInt64(value);

        if (SerializableGraph.IsAllowedPrimitive(t))
            return value;

        // Allow embedding captured SerializingState nodes inside the graph.
        // This is used by higher-level serializers that want to carry
        // pre-captured state trees without re-encoding them.
        if (SerializableGraph.IsSerializingState(t))
            return value;

        if (t.IsArray)
        {
            var elemType = t.GetElementType()!;
            var arr = (Array)value;
            var list = new List<object?>(arr.Length);
            for (int i = 0; i < arr.Length; i++)
                list.Add(CaptureValue(arr.GetValue(i), elemType));
            return list;
        }

        if (SerializableGraph.TryGetListElementType(t, out var listElem))
        {
            var result = new List<object?>();
            foreach (var it in (IEnumerable)value)
                result.Add(CaptureValue(it, listElem));
            return result;
        }

        // Dictionary support
        if (value is IDictionary dict)
        {
            var node = new Dictionary<object?, object?>(dict.Count);
            foreach (DictionaryEntry e in dict)
            {
                var k = CaptureValue(e.Key, e.Key?.GetType() ?? typeof(object));
                var v = CaptureValue(e.Value, e.Value?.GetType() ?? typeof(object));
                node[k] = v;
            }
            return node;
        }

        // If declared type is dictionary-like but runtime isn't IDictionary, it's an error.
        if (SerializableGraph.TryGetDictionaryTypes(t, out _, out _))
        {
            throw new InvalidOperationException(
                $"CaptureValue: Declared type is dictionary-like ('{t.FullName}') but runtime value is not IDictionary ('{value.GetType().FullName}').");
        }

        // Polymorphic ISerializable wrapper uses Dictionary<string, object?> in-memory.
        if (value is ISerializable s)
        {
            return new Dictionary<string, object?>(2, StringComparer.Ordinal)
            {
                ["__type"] = s.GetType().AssemblyQualifiedName ?? s.GetType().FullName ?? s.GetType().Name,
                ["data"] = s.CaptureState()
            };
        }

        if (t.IsValueType)
            return value;

        throw new InvalidOperationException($"Unsupported CaptureValue type: {t.FullName}");
    }

    private static object? RestoreValue(object? raw, Type declaredType)
    {
        var isNullable = Nullable.GetUnderlyingType(declaredType) != null;
        var t = Nullable.GetUnderlyingType(declaredType) ?? declaredType;

        if (raw == null)
        {
            if (!t.IsValueType || isNullable) return null;
            throw new InvalidOperationException($"RestoreValue failed: value-type '{t.FullName}' cannot be null.");
        }

        if (t.IsEnum)
        {
            var n = Convert.ToInt64(raw);
            return Enum.ToObject(t, n);
        }

        if (SerializableGraph.IsAllowedPrimitive(t))
        {
            if (t == typeof(string)) return raw as string;
            if (t == typeof(Guid)) return raw is Guid g ? g : Guid.Parse(raw.ToString() ?? Guid.Empty.ToString());

            if (t == typeof(bool)) return Convert.ToBoolean(raw);
            if (t == typeof(byte)) return Convert.ToByte(raw);
            if (t == typeof(sbyte)) return Convert.ToSByte(raw);
            if (t == typeof(short)) return Convert.ToInt16(raw);
            if (t == typeof(ushort)) return Convert.ToUInt16(raw);
            if (t == typeof(int)) return Convert.ToInt32(raw);
            if (t == typeof(uint)) return Convert.ToUInt32(raw);
            if (t == typeof(long)) return Convert.ToInt64(raw);
            if (t == typeof(ulong)) return Convert.ToUInt64(raw);
            if (t == typeof(float)) return Convert.ToSingle(raw);
            if (t == typeof(double)) return Convert.ToDouble(raw);
            if (t == typeof(decimal)) return Convert.ToDecimal(raw);

            throw new InvalidOperationException($"Unexpected primitive type: {t.FullName}");
        }
        
        if (SerializableGraph.IsSerializingState(t))
        {
            if (raw is SerializingState ss) return ss;
            throw new InvalidOperationException(
                $"RestoreValue failed: expected SerializingState node for '{t.FullName}', got '{raw.GetType().FullName}'.");
        }


        if (t.IsArray)
        {
            if (raw is not IReadOnlyList<object?> list)
                throw new InvalidOperationException($"Array node must be a list. Got: {raw.GetType().FullName}");

            var elemType = t.GetElementType()!;
            var arr = Array.CreateInstance(elemType, list.Count);

            for (int i = 0; i < list.Count; i++)
                arr.SetValue(RestoreValue(list[i], elemType), i);

            return arr;
        }

        if (SerializableGraph.TryGetListElementType(t, out var listElem))
        {
            if (raw is not IReadOnlyList<object?> list)
                throw new InvalidOperationException($"List node must be a list. Got: {raw.GetType().FullName}");

            var concreteType = typeof(List<>).MakeGenericType(listElem);
            var concrete = (IList)Activator.CreateInstance(concreteType)!;

            for (int i = 0; i < list.Count; i++)
                concrete.Add(RestoreValue(list[i], listElem));

            return concrete;
        }

        if (SerializableGraph.TryGetDictionaryTypes(t, out var keyType, out var valueType))
        {
            if (raw is not IDictionary rawDict)
                throw new InvalidOperationException($"Dictionary node must be IDictionary. Got: {raw.GetType().FullName}");

            var concreteType = typeof(Dictionary<,>).MakeGenericType(keyType, valueType);
            var concrete = (IDictionary)Activator.CreateInstance(concreteType)!;

            foreach (DictionaryEntry e in rawDict)
            {
                var rk = RestoreValue(e.Key, keyType);
                var rv = RestoreValue(e.Value, valueType);
                concrete.Add(rk!, rv);
            }

            return concrete;
        }

        if (typeof(ISerializable).IsAssignableFrom(t))
        {
            if (raw is not IReadOnlyDictionary<string, object?> wrapper)
                throw new InvalidOperationException($"Serializable wrapper must be a dictionary. Got: {raw.GetType().FullName}");

            if (!wrapper.TryGetValue("data", out var dataObj) || dataObj is not SerializingState data)
                throw new InvalidOperationException("Serializable wrapper missing 'data' (SerializingState).");

            var runtimeType = t;
            if (wrapper.TryGetValue("__type", out var typeStrObj) && typeStrObj is string typeStr)
            {
                var resolved = Type.GetType(typeStr);
                if (resolved != null && t.IsAssignableFrom(resolved))
                    runtimeType = resolved;
            }

            var inst = CreateSerializableInstance(runtimeType);
            inst.RestoreState(data);
            return inst;
        }

        if (t.IsValueType)
            return raw;

        throw new InvalidOperationException($"Unsupported RestoreValue type: {t.FullName}");
    }
    
    public static ISerializable CreateSerializableInstance(Type runtimeType)
    {
        try
        {
            return (ISerializable)Activator.CreateInstance(runtimeType, nonPublic: true)!;
        }
        catch
        {
            return (ISerializable)RuntimeHelpers.GetUninitializedObject(runtimeType);
        }
    }
}
