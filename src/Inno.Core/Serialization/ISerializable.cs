using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Inno.Core.Logging;

namespace Inno.Core.Serialization;


/// <summary>
/// Marks an instance method to be invoked after <see cref="ISerializable.RestoreState"/> completes.
/// </summary>
/// <remarks>
/// The method must be parameterless and return void. Invocation order is base-type to derived-type.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, Inherited = true)]
public sealed class OnSerializableRestored : Attribute;


/// <summary>
/// Represents an object that can capture and restore its state using <see cref="SerializingState"/>.
/// </summary>
/// <remarks>
/// Only members annotated with <see cref="SerializablePropertyAttribute"/> participate in the state graph.
/// </remarks>
public interface ISerializable
{
    #region Public API

    /// <summary>
    /// Returns the serialized properties declared on this instance type.
    /// </summary>
    /// <returns>A stable, ordered list of serialized properties for this instance.</returns>
    public IReadOnlyList<SerializedProperty> GetSerializedProperties()
    {
        var slots = GetSlots(GetType());
        var result = new List<SerializedProperty>(slots.Length);

        foreach (var s in slots)
        {
            if (s.visibility == PropertyVisibility.Hide)
                continue;
            
            var noSetterAllowed = 
                ((s.visibility & PropertyVisibility.RuntimeSet) == 0);
            
            result.Add(new SerializedProperty(
                s.name,
                s.type,
                () => s.getter(this),
                v =>
                {
                    if (noSetterAllowed)
                    {
                        Log.Warn($"SerializedProperty {s.name} is not allowed to set its value.");
                        return;
                    }

                    s.setter(this, v);
                },
                s.visibility));
        }

        return result;
    }

    /// <summary>
    /// Captures this instance into an in-memory state tree.
    /// </summary>
    /// <returns>A <see cref="SerializingState"/> representing this instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a member value is not supported by the serialization graph.</exception>
    public SerializingState CaptureState()
    {
        var slots = GetSlots(GetType());
        var node = new Dictionary<string, object?>(slots.Length, StringComparer.Ordinal);

        foreach (var s in slots)
        {
            if ((s.visibility & PropertyVisibility.Serialize) == 0)
            {
                continue;
            }
            
            node[s.name] = CaptureValue(s.getter(this), s.type);
        }

        return new SerializingState(node);
    }

    /// <summary>
    /// Restores this instance from a previously captured state.
    /// </summary>
    /// <param name="state">The source state.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="state"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a state value cannot be converted to the declared member type.</exception>
    /// <remarks>
    /// After restoration, methods annotated with <see cref="OnSerializableRestored"/> are invoked.
    /// </remarks>
    public void RestoreState(SerializingState state)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));

        var slots = GetSlots(GetType());
        foreach (var s in slots)
        {
            if ((s.visibility & PropertyVisibility.Deserialize) == 0)
            {
                continue;
            }

            if (!state.values.TryGetValue(s.name, out var raw))
                continue;

            s.setter(this, RestoreValue(raw, s.type));
        }

        InvokeAfterRestoreHooks();
    }

    /// <summary>
    /// Creates an instance for a runtime <see cref="Type"/> that implements <see cref="ISerializable"/>.
    /// </summary>
    /// <param name="runtimeType">The concrete runtime type.</param>
    /// <returns>An instance of <paramref name="runtimeType"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="runtimeType"/> is null.</exception>
    /// <exception cref="InvalidCastException">Thrown when <paramref name="runtimeType"/> does not implement <see cref="ISerializable"/>.</exception>
    /// <remarks>
    /// The creation strategy prefers a non-public parameterless constructor; otherwise it falls back to an uninitialized object.
    /// </remarks>
    /// <example>
    /// <code>
    /// var inst = ISerializable.CreateSerializableInstance(typeof(MyComponent));
    /// inst.RestoreState(state);
    /// </code>
    /// </example>
    public static ISerializable CreateSerializableInstance(Type runtimeType)
    {
        if (runtimeType == null) throw new ArgumentNullException(nameof(runtimeType));
        if (!typeof(ISerializable).IsAssignableFrom(runtimeType))
            throw new InvalidCastException($"Type '{runtimeType.FullName}' does not implement {nameof(ISerializable)}.");

        try
        {
            return (ISerializable)Activator.CreateInstance(runtimeType, nonPublic: true)!;
        }
        catch
        {
            return (ISerializable)RuntimeHelpers.GetUninitializedObject(runtimeType);
        }
    }

    #endregion

    #region Internal Validation API

    internal static (string name, Type type)[] GetSlotsForValidation(Type type)
    {
        var slots = GetSlots(type);
        var arr = new (string name, Type type)[slots.Length];

        for (var i = 0; i < slots.Length; i++)
            arr[i] = (slots[i].name, slots[i].type);

        return arr;
    }

    #endregion

    #region Slot Cache

    private sealed record MemberSlot(
        string name,
        Type type,
        Func<object, object?> getter,
        Action<object, object?> setter,
        PropertyVisibility visibility,
        int orderKey);

    private static readonly ConcurrentDictionary<Type, MemberSlot[]> SLOT_CACHE = new();

    private static MemberSlot[] GetSlots(Type type) => SLOT_CACHE.GetOrAdd(type, BuildSlots);

    private static MemberSlot[] BuildSlots(Type type)
    {
        const BindingFlags c_declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        var chain = new List<Type>(8);
        for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            chain.Add(t);
        chain.Reverse();

        var chainIndex = chain
            .Select((t, i) => (t, i))
            .ToDictionary(x => x.t, x => x.i);

        var list = new List<MemberSlot>(64);

        foreach (var t in chain)
        {
            var depth = chainIndex[t];

            foreach (var p in t.GetProperties(c_declared))
            {
                var attr = p.GetCustomAttribute<SerializablePropertyAttribute>(inherit: true);
                if (attr == null)
                    continue;

                if (p.GetIndexParameters().Length != 0)
                    throw new InvalidOperationException($"{type.FullName}.{p.Name} cannot be an indexer to be [SerializableProperty].");

                if (!p.CanRead)
                    throw new InvalidOperationException($"{type.FullName}.{p.Name} must be readable to be [SerializableProperty].");

                var noSetterAllowed = (attr.propertyVisibility & PropertyVisibility.Deserialize) == 0;
                var setMethod = p.GetSetMethod(nonPublic: true);
                Action<object, object?> setter;
                if (setMethod == null)
                {
                    setter = (_, _) => Log.Error($"SerializedProperty {p.Name} has no setter defined.");
                }
                else
                {
                    setter = setMethod.IsPublic
                        ? (obj, value) => p.SetValue(obj, value)
                        : (obj, value) => setMethod.Invoke(obj, [value]);
                }
                
                if (!noSetterAllowed && setMethod == null)
                    throw new InvalidOperationException($"{type.FullName}.{p.Name} must have setter for its [SerializableProperty].");
                
                SerializableGraph.ValidateAllowedTypeGraph(p.PropertyType, $"{type.FullName}.{p.Name}");

                list.Add(new MemberSlot(
                    p.Name,
                    p.PropertyType,
                    obj => p.GetValue(obj),
                    setter,
                    attr.propertyVisibility,
                    (depth << 24) ^ p.MetadataToken));
            }

            foreach (var f in t.GetFields(c_declared))
            {
                var attr = f.GetCustomAttribute<SerializablePropertyAttribute>(inherit: true);
                if (attr == null)
                    continue;

                var noSetterAllowed = 
                    (attr.propertyVisibility & PropertyVisibility.Deserialize) == 0;

                Action<object, object?> setter;
                if (f.IsInitOnly)
                {
                    setter = (_, _) => Log.Error($"SerializedProperty {f.Name} is initialized only.");
                }
                else
                {
                    setter = f.SetValue;
                }
                
                if (!noSetterAllowed && f.IsInitOnly)
                    throw new InvalidOperationException($"{type.FullName}.{f.Name} is readonly; it must be writable for its [SerializableProperty].");
                
                SerializableGraph.ValidateAllowedTypeGraph(f.FieldType, $"{type.FullName}.{f.Name}");

                list.Add(new MemberSlot(
                    f.Name,
                    f.FieldType,
                    obj => f.GetValue(obj),
                    setter,
                    attr.propertyVisibility,
                    (depth << 24) ^ f.MetadataToken));
            }
        }

        return list
            .GroupBy(s => s.name, StringComparer.Ordinal)
            .Select(g => g.OrderBy(x => x.orderKey).Last())
            .OrderBy(s => s.orderKey)
            .ToArray();
    }

    #endregion

    #region Value Conversions

    private static object? CaptureValue(object? value, Type declaredType)
    {
        if (value == null) return null;

        var t = Nullable.GetUnderlyingType(declaredType) ?? declaredType;

        if (t.IsEnum) return Convert.ToInt64(value);
        if (SerializableGraph.IsAllowedPrimitive(t)) return value;
        if (SerializableGraph.IsSerializingState(t)) return value;

        if (t.IsArray)
        {
            var elemType = t.GetElementType()!;
            var arr = (Array)value;
            var list = new List<object?>(arr.Length);
            for (var i = 0; i < arr.Length; i++)
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

        if (value is IDictionary dict)
        {
            var node = new Dictionary<object?, object?>(dict.Count);
            foreach (DictionaryEntry e in dict)
                node[CaptureValue(e.Key, e.Key?.GetType() ?? typeof(object))] = CaptureValue(e.Value, e.Value?.GetType() ?? typeof(object));
            return node;
        }

        if (SerializableGraph.TryGetDictionaryTypes(t, out _, out _))
            throw new InvalidOperationException($"CaptureValue: Declared type is dictionary-like ('{t.FullName}') but runtime value is not IDictionary ('{value.GetType().FullName}').");

        if (value is ISerializable s)
        {
            return new Dictionary<string, object?>(2, StringComparer.Ordinal)
            {
                ["__type"] = s.GetType().AssemblyQualifiedName ?? s.GetType().FullName ?? s.GetType().Name,
                ["data"] = s.CaptureState()
            };
        }

        if (t.IsValueType) return value;

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

        if (t.IsEnum) return Enum.ToObject(t, Convert.ToInt64(raw));

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
            throw new InvalidOperationException($"RestoreValue failed: expected SerializingState node for '{t.FullName}', got '{raw.GetType().FullName}'.");
        }

        if (t.IsArray)
        {
            if (raw is not IReadOnlyList<object?> list)
                throw new InvalidOperationException($"Array node must be a list. Got: {raw.GetType().FullName}");

            var elemType = t.GetElementType()!;
            var arr = Array.CreateInstance(elemType, list.Count);
            for (var i = 0; i < list.Count; i++)
                arr.SetValue(RestoreValue(list[i], elemType), i);

            return arr;
        }

        if (SerializableGraph.TryGetListElementType(t, out var listElem))
        {
            if (raw is not IReadOnlyList<object?> list)
                throw new InvalidOperationException($"List node must be a list. Got: {raw.GetType().FullName}");

            var concreteType = typeof(List<>).MakeGenericType(listElem);
            var concrete = (IList)Activator.CreateInstance(concreteType)!;

            for (var i = 0; i < list.Count; i++)
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
                concrete.Add(RestoreValue(e.Key, keyType)!, RestoreValue(e.Value, valueType));

            return concrete;
        }

        if (typeof(ISerializable).IsAssignableFrom(t))
        {
            var wrapper = CoerceToStringKeyDictionary(raw);

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

        if (t.IsValueType) return raw;

        throw new InvalidOperationException($"Unsupported RestoreValue type: {t.FullName}");
    }

    private static Dictionary<string, object?> CoerceToStringKeyDictionary(object raw)
    {
        if (raw is Dictionary<string, object?> sdict) return sdict;

        if (raw is IDictionary dict)
        {
            var result = new Dictionary<string, object?>(dict.Count, StringComparer.Ordinal);

            foreach (DictionaryEntry e in dict)
            {
                if (e.Key is not string k)
                    throw new InvalidOperationException($"Serializable wrapper dict keys must be strings. Got key type: {e.Key?.GetType().FullName ?? "null"}");

                result[k] = e.Value;
            }

            return result;
        }

        throw new InvalidOperationException($"Serializable wrapper must be a dictionary. Got: {raw.GetType().FullName}");
    }

    #endregion

    #region Restore Hooks

    private void InvokeAfterRestoreHooks()
    {
        const BindingFlags c_declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var type = GetType();

        var chain = new List<Type>(8);
        for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            chain.Add(t);
        chain.Reverse();

        foreach (var t in chain)
        {
            foreach (var m in t.GetMethods(c_declared))
            {
                if (m.GetCustomAttribute<OnSerializableRestored>(inherit: true) == null)
                    continue;

                if (m.GetParameters().Length != 0)
                    throw new InvalidOperationException($"{type.FullName}.{m.Name} must have 0 parameters.");
                if (m.ReturnType != typeof(void))
                    throw new InvalidOperationException($"{type.FullName}.{m.Name} must return void.");

                m.Invoke(this, null);
            }
        }
    }

    #endregion
}
