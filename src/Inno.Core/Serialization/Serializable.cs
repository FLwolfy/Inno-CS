using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Inno.Core.Serialization;

/// <summary>
/// Base class for editor/runtime state that is allowed to be saved/restored.
///
/// Contract:
/// - Only members marked with [SerializableProperty] participate.
/// - Marked member types must be either:
///   (1) JSON-primitive-like (bool/number/string/Guid/enum)
///   (2) supported engine value types (e.g. Inno.Core.Math structs)
///   (3) another Serializable (nested)
///
/// This is intentionally stricter than "serialize anything" to avoid cycles,
/// runtime handles (e.g. Sprite/Texture/GPU objects), and ctor requirements.
/// </summary>
public abstract class Serializable
{
    private sealed record MemberSlot(
        string name,
        Type type,
        Func<object, object?> getter,
        Action<object, object?> setter,
        SerializedProperty.PropertyVisibility visibility,
        int orderKey
    );

    private static readonly ConcurrentDictionary<Type, MemberSlot[]> SLOT_CACHE = new();

    /// <summary>
    /// Returns the editor-facing serialized properties (used by Inspector).
    /// </summary>
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

    /// <summary>
    /// Captures a deep, cycle-free state node consisting only of allowed types.
    /// Values are primitives/value-types, or nested dictionaries for Serializable.
    /// </summary>
    public Dictionary<string, object?> CaptureState()
    {
        var slots = GetSlots(GetType());
        var node = new Dictionary<string, object?>(slots.Length, StringComparer.Ordinal);

        foreach (var s in slots)
        {
            var v = s.getter(this);
            node[s.name] = CaptureValue(v, s.type);
        }

        return node;
    }

    /// <summary>
    /// Restores state captured by <see cref="CaptureState"/>.
    /// </summary>
    public void RestoreState(IReadOnlyDictionary<string, object?> node)
    {
        var slots = GetSlots(GetType());
        foreach (var s in slots)
        {
            if (!node.TryGetValue(s.name, out var raw))
                continue;

            var v = RestoreValue(raw, s.type);
            s.setter(this, v);
        }
        
        OnAfterRestore();
    }
    
    /// <summary>
    /// Called after the restoration of this instance is done.
    /// </summary>
    protected virtual void OnAfterRestore() { }

    // -----------------
    // Slot reflection
    // -----------------
    private static MemberSlot[] GetSlots(Type type) => SLOT_CACHE.GetOrAdd(type, BuildSlots);

    private static MemberSlot[] BuildSlots(Type type)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var list = new List<MemberSlot>(32);

        // Properties
        foreach (var p in type.GetProperties(flags))
        {
            var attr = p.GetCustomAttribute<SerializablePropertyAttribute>(inherit: true);
            if (attr == null) continue;

            if (!p.CanRead || !p.CanWrite)
                throw new InvalidOperationException($"{type.FullName}.{p.Name} must have getter+setter to be [SerializableProperty].");

            ValidateAllowedMemberType(p.PropertyType, $"{type.FullName}.{p.Name}");

            Func<object, object?> getter = obj => p.GetValue(obj);
            Action<object, object?> setter = (obj, v) => p.SetValue(obj, v);
            list.Add(new MemberSlot(p.Name, p.PropertyType, getter, setter, attr.propertyVisibility, p.MetadataToken));
        }

        // Fields
        foreach (var f in type.GetFields(flags))
        {
            var attr = f.GetCustomAttribute<SerializablePropertyAttribute>(inherit: true);
            if (attr == null) continue;

            if (f.IsInitOnly)
                throw new InvalidOperationException($"{type.FullName}.{f.Name} is readonly; it cannot be [SerializableProperty].");

            ValidateAllowedMemberType(f.FieldType, $"{type.FullName}.{f.Name}");

            Func<object, object?> getter = obj => f.GetValue(obj);
            Action<object, object?> setter = (obj, v) => f.SetValue(obj, v);
            list.Add(new MemberSlot(f.Name, f.FieldType, getter, setter, attr.propertyVisibility, f.MetadataToken));
        }

        // Stable order: metadata token roughly matches source order.
        return list.OrderBy(s => s.orderKey).ToArray();
    }

    private static void ValidateAllowedMemberType(Type type, string where)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;

        if (t.IsEnum) return;

        // "JSON primitive" + common scalar types
        if (t == typeof(bool) ||
            t == typeof(byte) || t == typeof(sbyte) ||
            t == typeof(short) || t == typeof(ushort) ||
            t == typeof(int) || t == typeof(uint) ||
            t == typeof(long) || t == typeof(ulong) ||
            t == typeof(float) || t == typeof(double) || t == typeof(decimal) ||
            t == typeof(string) ||
            t == typeof(Guid))
        {
            return;
        }

        // Nested Serializable
        if (typeof(Serializable).IsAssignableFrom(t))
            return;

        // Engine value-types (Vector2/3/4, Quaternion, Matrix, Color, etc.)
        // If you want to be stricter, replace this with an explicit whitelist.
        if (t.IsValueType && (t.Namespace?.StartsWith("Inno.Core.Math", StringComparison.Ordinal) ?? false))
            return;

        // AssetRef<T> is a value-type used widely across your pipeline.
        if (t.IsValueType && (t.FullName?.StartsWith("Inno.Assets.AssetRef`1", StringComparison.Ordinal) ?? false))
            return;

        throw new InvalidOperationException(
            $"{where} has unsupported [SerializableProperty] type '{t.FullName}'. " +
            "Allowed: JSON primitives (bool/number/string/Guid/enum), Inno.Core.Math value-types, AssetRef<T>, or Serializable.");
    }

    // -----------------
    // State capture/restore
    // -----------------

    private static object? CaptureValue(object? value, Type declaredType)
    {
        if (value == null) return null;

        var t = Nullable.GetUnderlyingType(declaredType) ?? declaredType;

        if (t.IsEnum)
            return Convert.ToInt64(value);

        if (t == typeof(string) || t == typeof(Guid))
            return value;

        if (t == typeof(bool) ||
            t == typeof(byte) || t == typeof(sbyte) ||
            t == typeof(short) || t == typeof(ushort) ||
            t == typeof(int) || t == typeof(uint) ||
            t == typeof(long) || t == typeof(ulong) ||
            t == typeof(float) || t == typeof(double) || t == typeof(decimal))
        {
            return value;
        }

        if (value is Serializable s)
        {
            // Store runtime type for safety (polymorphism).
            return new Dictionary<string, object?>(2, StringComparer.Ordinal)
            {
                ["__type"] = s.GetType().AssemblyQualifiedName ?? s.GetType().FullName ?? s.GetType().Name,
                ["data"] = s.CaptureState()
            };
        }

        // Engine value-types and AssetRef<T> are copied by value.
        if (t.IsValueType)
            return value;

        // Should be unreachable due to validation.
        throw new InvalidOperationException($"Unsupported CaptureValue type: {t.FullName}");
    }

    private static object? RestoreValue(object? raw, Type declaredType)
    {
        var isNullable = Nullable.GetUnderlyingType(declaredType) != null;
        var t = Nullable.GetUnderlyingType(declaredType) ?? declaredType;

        if (raw == null)
        {
            // Nullable<T> or reference types may legitimately be null
            if (!t.IsValueType || isNullable)
                return null;

            // Value-types must never restore from null
            throw new InvalidOperationException(
                $"RestoreValue failed: value-type '{t.FullName}' cannot be null.");
        }

        if (t.IsEnum)
        {
            var n = Convert.ToInt64(raw);
            return Enum.ToObject(t, n);
        }

        if (t == typeof(string))
            return raw as string;

        if (t == typeof(Guid))
            return raw is Guid g ? g : Guid.Parse(raw.ToString() ?? Guid.Empty.ToString());

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

        if (typeof(Serializable).IsAssignableFrom(t))
        {
            if (raw is not IReadOnlyDictionary<string, object?> wrapper)
                throw new InvalidOperationException($"Serializable node must be a dictionary. Got: {raw.GetType().FullName}");

            if (!wrapper.TryGetValue("data", out var dataObj) || dataObj is not IReadOnlyDictionary<string, object?> data)
                throw new InvalidOperationException("Serializable node missing 'data'.");

            // Resolve runtime type if present.
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

        // Value-types (Vector/Quaternion/Matrix, AssetRef<T>) are stored as boxed values.
        if (t.IsValueType)
            return raw;

        throw new InvalidOperationException($"Unsupported RestoreValue type: {t.FullName}");
    }
    
    private static Serializable CreateSerializableInstance(Type runtimeType)
    {
        // Prefer parameterless ctor if it exists (public or non-public).
        // This keeps invariants for types that rely on ctor initialization.
        try
        {
            return (Serializable)Activator.CreateInstance(runtimeType, nonPublic: true)!;
        }
        catch
        {
            // Fallback: create without running any ctor.
            // .NET 8/9: RuntimeHelpers.GetUninitializedObject is the supported API.
            return (Serializable)RuntimeHelpers.GetUninitializedObject(runtimeType);
        }
    }

}
