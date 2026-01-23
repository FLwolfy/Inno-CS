using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
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
/// 5) Containers: T[] / List / Dictionary (and compatible interfaces),
///
///
/// Visibility rules (applies to BOTH structs and ISerializable):
/// - Show: participate in serialize + deserialize
/// - Hide: participate in neither serialize nor deserialize
/// - ReadOnly: participate in serialize only (skip deserialize); can have no setter (property) / can be readonly field
///
/// Notes:
/// - No Dictionary is allowed in the graph (except internal SerializingState key map).
/// - Binary serialization is provided by SerializingState (NOT by ISerializable).
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


    private static MemberSlot[] GetSlots(Type type)
    {
        return SLOT_CACHE.GetOrAdd(type, BuildSlots);
    }

    private static MemberSlot[] BuildSlots(Type type)
    {
        const BindingFlags c_declaredFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        var chain = new List<Type>(8);
        for (var t = type; t != null && t != typeof(object); t = t.BaseType)
        {
            chain.Add(t);
        }
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

                ValidateAllowedTypeGraph(p.PropertyType, $"{type.FullName}.{p.Name}");

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

                ValidateAllowedTypeGraph(f.FieldType, $"{type.FullName}.{f.Name}");

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

        // we want to allow private/protected setters
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
    //  Allowed type graph rules (NO Dictionary in user graph)
    // =====================================================================

    private static void ValidateAllowedTypeGraph(Type type, string where)
    {
        var visited = new HashSet<Type>();
        ValidateAllowedTypeGraphRec(type, where, visited, forbidISerializable: false);
    }

    private static void ValidateAllowedTypeGraphRec(
        Type type,
        string where,
        HashSet<Type> visited,
        bool forbidISerializable
    )
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        if (!visited.Add(t)) return;

        if (t.IsEnum) return;
        if (IsAllowedPrimitive(t)) return;

        if (t.IsArray)
        {
            var elem = t.GetElementType()!;
            ValidateAllowedTypeGraphRec(elem, $"{where}[]", visited, forbidISerializable);
            return;
        }

        if (TryGetListElementType(t, out var listElem))
        {
            ValidateAllowedTypeGraphRec(listElem, $"{where}<T>", visited, forbidISerializable);
            return;
        }

        if (TryGetDictionaryTypes(t, out var kType, out var vType))
        {
            ValidateAllowedTypeGraphRec(kType, $"{where}<K>", visited, forbidISerializable);
            ValidateAllowedTypeGraphRec(vType, $"{where}<V>", visited, forbidISerializable);
            return;
        }

        if (typeof(ISerializable).IsAssignableFrom(t))
        {
            if (forbidISerializable)
            {
                throw new InvalidOperationException(
                    $"{where} contains '{t.FullName}', but ISerializable is forbidden inside a non-ISerializable struct graph.");
            }

            var slots = GetSlots(t); // already applies Hide/ReadOnly rules
            for (int i = 0; i < slots.Length; i++)
                ValidateAllowedTypeGraphRec(slots[i].type, $"{t.FullName}.{slots[i].name}", visited, forbidISerializable: false);

            return;
        }

        if (t.IsValueType)
        {
            // pure struct (non-ISerializable): default include public members,
            // but Hide excludes; ReadOnly still participates in type-graph validation.
            var nextForbid = true;

            foreach (var f in GetStructSerializableFields(t))
                ValidateAllowedTypeGraphRec(f.FieldType, $"{t.FullName}.{f.Name}", visited, nextForbid);

            foreach (var p in GetStructSerializableProperties(t))
                ValidateAllowedTypeGraphRec(p.PropertyType, $"{t.FullName}.{p.Name}", visited, nextForbid);

            return;
        }

        throw new InvalidOperationException(
            $"{where} has unsupported type '{t.FullName}'. " +
            "Allowed: primitives, enums, structs (recursive), ISerializable (recursive), arrays, List<T>.");
    }

    private static bool IsAllowedPrimitive(Type t)
    {
        return t == typeof(bool)
               || t == typeof(byte) || t == typeof(sbyte)
               || t == typeof(short) || t == typeof(ushort)
               || t == typeof(int) || t == typeof(uint)
               || t == typeof(long) || t == typeof(ulong)
               || t == typeof(float) || t == typeof(double)
               || t == typeof(decimal)
               || t == typeof(string)
               || t == typeof(Guid);
    }

    private static bool TryGetListElementType(Type t, out Type elem)
    {
        elem = null!;

        if (t.IsArray)
            return false;

        if (t.IsGenericType)
        {
            var def = t.GetGenericTypeDefinition();
            if (def == typeof(List<>) || def == typeof(IList<>) || def == typeof(IReadOnlyList<>))
            {
                elem = t.GetGenericArguments()[0];
                return true;
            }
        }

        var ilist = t.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IList<>));
        if (ilist != null)
        {
            elem = ilist.GetGenericArguments()[0];
            return true;
        }

        var irolist = t.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IReadOnlyList<>));
        if (irolist != null)
        {
            elem = irolist.GetGenericArguments()[0];
            return true;
        }

        return false;
    }
    
    private static bool TryGetDictionaryTypes(Type t, out Type keyType, out Type valueType)
    {
        keyType = null!;
        valueType = null!;

        if (t.IsGenericType)
        {
            var def = t.GetGenericTypeDefinition();
            if (def == typeof(Dictionary<,>) || def == typeof(IDictionary<,>) || def == typeof(IReadOnlyDictionary<,>))
            {
                var args = t.GetGenericArguments();
                keyType = args[0];
                valueType = args[1];
                return true;
            }
        }

        // Scan interfaces for IDictionary<,> / IReadOnlyDictionary<,>
        foreach (var i in t.GetInterfaces())
        {
            if (!i.IsGenericType) continue;
            var def = i.GetGenericTypeDefinition();
            if (def == typeof(IDictionary<,>) || def == typeof(IReadOnlyDictionary<,>))
            {
                var args = i.GetGenericArguments();
                keyType = args[0];
                valueType = args[1];
                return true;
            }
        }

        return false;
    }


    private static bool IsDictionaryLike(Type t)
    {
        if (t.IsGenericType)
        {
            var def = t.GetGenericTypeDefinition();
            if (def == typeof(Dictionary<,>) || def == typeof(IDictionary<,>) || def == typeof(IReadOnlyDictionary<,>))
                return true;
        }

        if (typeof(IDictionary).IsAssignableFrom(t))
            return true;

        return t.GetInterfaces().Any(i =>
            i.IsGenericType && (
                i.GetGenericTypeDefinition() == typeof(IDictionary<,>)
                || i.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)
            ));
    }

    private static SerializedProperty.PropertyVisibility GetVisibilityOrShow(MemberInfo m)
    {
        var a = m.GetCustomAttribute<SerializablePropertyAttribute>(inherit: true);
        return a?.propertyVisibility ?? SerializedProperty.PropertyVisibility.Show;
    }

    private static FieldInfo[] GetStructSerializableFields(Type t)
    {
        return t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(f => GetVisibilityOrShow(f) != SerializedProperty.PropertyVisibility.Hide)
            .OrderBy(f => f.MetadataToken)
            .ToArray();
    }

    private static PropertyInfo[] GetStructSerializableProperties(Type t)
    {
        return t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(p =>
            {
                if (p.GetIndexParameters().Length != 0) return false;

                var vis = GetVisibilityOrShow(p);
                if (vis == SerializedProperty.PropertyVisibility.Hide) return false;

                if (!p.CanRead) return false;

                // Show => must be writable; ReadOnly => can be non-writable
                if (vis == SerializedProperty.PropertyVisibility.Show && p.GetSetMethod(nonPublic: true) == null) return false;

                return true;
            })
            .OrderBy(p => p.MetadataToken)
            .ToArray();
    }

    // =====================================================================
    //  State capture/restore value conversions (in-memory graph)
    //  (NO Dictionary user nodes; SerializingState uses an internal Dictionary map)
    // =====================================================================

    private static object? CaptureValue(object? value, Type declaredType)
    {
        if (value == null) return null;

        var t = Nullable.GetUnderlyingType(declaredType) ?? declaredType;

        if (t.IsEnum)
            return Convert.ToInt64(value);

        if (IsAllowedPrimitive(t))
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

        if (TryGetListElementType(t, out var listElem))
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
        if (TryGetDictionaryTypes(t, out _, out _))
        {
            throw new InvalidOperationException(
                $"CaptureValue: Declared type is dictionary-like ('{t.FullName}') but runtime value is not IDictionary ('{value.GetType().FullName}').");
        }

        // NOTE: Polymorphic ISerializable wrapper uses dictionary in-memory.
        // If you want *strictly no* IDictionary-shaped nodes anywhere, replace this with a dedicated node type.
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

        if (IsAllowedPrimitive(t))
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

        if (TryGetListElementType(t, out var listElem))
        {
            if (raw is not IReadOnlyList<object?> list)
                throw new InvalidOperationException($"List node must be a list. Got: {raw.GetType().FullName}");

            var concreteType = typeof(List<>).MakeGenericType(listElem);
            var concrete = (IList)Activator.CreateInstance(concreteType)!;

            for (int i = 0; i < list.Count; i++)
                concrete.Add(RestoreValue(list[i], listElem));

            return concrete;
        }

        if (TryGetDictionaryTypes(t, out var keyType, out var valueType))
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

    // =====================================================================
    //  Binary codec internals (used by SerializingState)
    //  (NO Dictionary nodes except internal SerializingState key map)
    // =====================================================================

    private enum BinKind : byte
    {
        Null = 0,

        Primitive = 1,
        Enum = 2,

        Array = 10,
        List = 11,
        Dict = 12,

        State = 20,
        Serializable = 21,
        Struct = 22,
    }

    private enum PrimKind : byte
    {
        Bool = 1,
        Byte = 2,
        SByte = 3,
        Int16 = 4,
        UInt16 = 5,
        Int32 = 6,
        UInt32 = 7,
        Int64 = 8,
        UInt64 = 9,
        Single = 10,
        Double = 11,
        Decimal = 12,
        String = 13,
        Guid = 14,
    }

    internal static void WriteNode(BinaryWriter bw, object? value)
    {
        if (value == null)
        {
            bw.Write((byte)BinKind.Null);
            return;
        }

        if (value is SerializingState state)
        {
            WriteState(bw, state);
            return;
        }

        var t = value.GetType();

        if (t.IsEnum)
        {
            bw.Write((byte)BinKind.Enum);
            bw.Write(t.AssemblyQualifiedName ?? t.FullName ?? t.Name);
            bw.Write(Convert.ToInt64(value));
            return;
        }

        if (IsAllowedPrimitive(t))
        {
            bw.Write((byte)BinKind.Primitive);
            WritePrimitive(bw, value, t);
            return;
        }

        if (t.IsArray)
        {
            var arr = (Array)value;
            bw.Write((byte)BinKind.Array);
            bw.Write(arr.Length);
            for (int i = 0; i < arr.Length; i++)
                WriteNode(bw, arr.GetValue(i));
            return;
        }

        if (value is IDictionary dict)
        {
            bw.Write((byte)BinKind.Dict);

            var entries = new List<DictionaryEntry>(dict.Count);
            foreach (DictionaryEntry e in dict) entries.Add(e);

            bool allStringKey = true;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Key is not string) { allStringKey = false; break; }
            }

            if (allStringKey)
            {
                entries.Sort((a, b) => StringComparer.Ordinal.Compare((string)a.Key, (string)b.Key));
            }

            bw.Write(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                WriteNode(bw, entries[i].Key);
                WriteNode(bw, entries[i].Value);
            }

            return;
        }

        if (value is IEnumerable en && value is not string)
        {
            var tmp = new List<object?>();
            foreach (var it in en) tmp.Add(it);

            bw.Write((byte)BinKind.List);
            bw.Write(tmp.Count);
            for (int i = 0; i < tmp.Count; i++)
                WriteNode(bw, tmp[i]);

            return;
        }

        if (value is ISerializable ser)
        {
            bw.Write((byte)BinKind.Serializable);
            var rt = ser.GetType();
            bw.Write(rt.AssemblyQualifiedName ?? rt.FullName ?? rt.Name);

            ValidateAllowedTypeGraph(rt, $"BinaryWrite({rt.FullName})");

            WriteState(bw, ser.CaptureState());
            return;
        }

        if (t.IsValueType)
        {
            ValidateAllowedTypeGraph(t, $"BinaryWrite({t.FullName})");

            bw.Write((byte)BinKind.Struct);
            bw.Write(t.AssemblyQualifiedName ?? t.FullName ?? t.Name);

            var fields = GetStructSerializableFields(t);
            var props = GetStructSerializableProperties(t);

            bw.Write(fields.Length + props.Length);

            for (int i = 0; i < fields.Length; i++)
            {
                var f = fields[i];
                bw.Write("F:" + f.Name);
                WriteNode(bw, f.GetValue(value));
            }

            for (int i = 0; i < props.Length; i++)
            {
                var p = props[i];
                bw.Write("P:" + p.Name);
                WriteNode(bw, p.GetValue(value));
            }

            return;
        }

        throw new InvalidDataException($"Unsupported node runtime type: {t.FullName}");
    }

    internal static object? ReadNode(BinaryReader br)
    {
        var kind = (BinKind)br.ReadByte();
        switch (kind)
        {
            case BinKind.Null:
                return null;

            case BinKind.Primitive:
                return ReadPrimitive(br);

            case BinKind.Enum:
            {
                var enumTypeName = br.ReadString();
                var enumType = Type.GetType(enumTypeName)
                               ?? throw new InvalidDataException($"Could not resolve enum type '{enumTypeName}'.");
                var n = br.ReadInt64();
                return Enum.ToObject(enumType, n);
            }

            case BinKind.Array:
            {
                int len = br.ReadInt32();
                var arr = new object?[len];
                for (int i = 0; i < len; i++)
                    arr[i] = ReadNode(br);
                return arr;
            }

            case BinKind.List:
            {
                int count = br.ReadInt32();
                var list = new List<object?>(System.Math.Max(0, count));
                for (int i = 0; i < count; i++)
                    list.Add(ReadNode(br));
                return list;
            }
            
            case BinKind.Dict:
            {
                int count = br.ReadInt32();
                var map = new Dictionary<object, object?>(System.Math.Max(0, count));
                for (int i = 0; i < count; i++)
                {
                    var k = ReadNode(br);
                    var v = ReadNode(br);
                    map[k] = v;
                }
                return map;
            }

            case BinKind.State:
                return ReadStateAfterKind(br);

            case BinKind.Serializable:
            {
                var typeName = br.ReadString();
                var rt = Type.GetType(typeName)
                         ?? throw new InvalidDataException($"Could not resolve ISerializable type '{typeName}'.");

                if (!typeof(ISerializable).IsAssignableFrom(rt))
                    throw new InvalidDataException($"Type '{rt.FullName}' is not ISerializable.");

                ValidateAllowedTypeGraph(rt, $"BinaryRead({rt.FullName})");

                var state = ReadState(br);
                var inst = CreateSerializableInstance(rt);
                inst.RestoreState(state);
                return inst;
            }

            case BinKind.Struct:
                return ReadStruct(br);

            default:
                throw new InvalidDataException($"Unknown binary node kind: {kind}");
        }
    }

    internal static void WriteState(BinaryWriter bw, SerializingState state)
    {
        bw.Write((byte)BinKind.State);

        var keys = state.values.Keys.ToList();
        keys.Sort(StringComparer.Ordinal);

        bw.Write(keys.Count);
        for (int i = 0; i < keys.Count; i++)
        {
            var k = keys[i];
            bw.Write(k);
            state.values.TryGetValue(k, out var v);
            WriteNode(bw, v);
        }
    }

    internal static SerializingState ReadState(BinaryReader br)
    {
        var kind = (BinKind)br.ReadByte();
        if (kind != BinKind.State)
            throw new InvalidDataException($"Expected State node, got {kind}.");

        return ReadStateAfterKind(br);
    }

    internal static SerializingState ReadStateAfterKind(BinaryReader br)
    {
        int count = br.ReadInt32();
        var map = new Dictionary<string, object?>(System.Math.Max(0, count), StringComparer.Ordinal);

        for (int i = 0; i < count; i++)
        {
            var key = br.ReadString();
            var val = ReadNode(br);
            map[key] = val;
        }

        return new SerializingState(map);
    }

    private static object ReadStruct(BinaryReader br)
    {
        var typeName = br.ReadString();
        var t = Type.GetType(typeName)
                ?? throw new InvalidDataException($"Could not resolve struct type '{typeName}'.");

        if (!t.IsValueType || t.IsEnum)
            throw new InvalidDataException($"Type '{t.FullName}' is not a struct value-type.");

        ValidateAllowedTypeGraph(t, $"BinaryRead({t.FullName})");

        object boxed = Activator.CreateInstance(t)!;

        int memberCount = br.ReadInt32();

        var fieldMap = GetStructSerializableFields(t)
            .ToDictionary(f => "F:" + f.Name, f => f, StringComparer.Ordinal);

        var propMap = GetStructSerializableProperties(t)
            .ToDictionary(p => "P:" + p.Name, p => p, StringComparer.Ordinal);

        for (int i = 0; i < memberCount; i++)
        {
            var key = br.ReadString();
            var val = ReadNode(br);

            if (fieldMap.TryGetValue(key, out var fi))
            {
                var vis = GetVisibilityOrShow(fi);
                if (vis != SerializedProperty.PropertyVisibility.ReadOnly && !fi.IsInitOnly)
                    fi.SetValue(boxed, RestoreValue(val, fi.FieldType));
                continue;
            }

            if (propMap.TryGetValue(key, out var pi))
            {
                var vis = GetVisibilityOrShow(pi);
                if (vis != SerializedProperty.PropertyVisibility.ReadOnly && pi.CanWrite)
                    pi.SetValue(boxed, RestoreValue(val, pi.PropertyType));
                continue;
            }

            // Unknown member: ignore for forward compatibility.
        }

        return boxed;
    }

    internal static void WritePrimitive(BinaryWriter bw, object value, Type t)
    {
        if (t == typeof(bool)) { bw.Write((byte)PrimKind.Bool); bw.Write((bool)value); return; }
        if (t == typeof(byte)) { bw.Write((byte)PrimKind.Byte); bw.Write((byte)value); return; }
        if (t == typeof(sbyte)) { bw.Write((byte)PrimKind.SByte); bw.Write((sbyte)value); return; }
        if (t == typeof(short)) { bw.Write((byte)PrimKind.Int16); bw.Write((short)value); return; }
        if (t == typeof(ushort)) { bw.Write((byte)PrimKind.UInt16); bw.Write((ushort)value); return; }
        if (t == typeof(int)) { bw.Write((byte)PrimKind.Int32); bw.Write((int)value); return; }
        if (t == typeof(uint)) { bw.Write((byte)PrimKind.UInt32); bw.Write((uint)value); return; }
        if (t == typeof(long)) { bw.Write((byte)PrimKind.Int64); bw.Write((long)value); return; }
        if (t == typeof(ulong)) { bw.Write((byte)PrimKind.UInt64); bw.Write((ulong)value); return; }
        if (t == typeof(float)) { bw.Write((byte)PrimKind.Single); bw.Write((float)value); return; }
        if (t == typeof(double)) { bw.Write((byte)PrimKind.Double); bw.Write((double)value); return; }
        if (t == typeof(decimal)) { bw.Write((byte)PrimKind.Decimal); bw.Write((decimal)value); return; }
        if (t == typeof(string)) { bw.Write((byte)PrimKind.String); bw.Write((string)value); return; }
        if (t == typeof(Guid))
        {
            bw.Write((byte)PrimKind.Guid);
            bw.Write(((Guid)value).ToByteArray());
            return;
        }

        throw new InvalidDataException($"Unsupported primitive type: {t.FullName}");
    }

    internal static object ReadPrimitive(BinaryReader br)
    {
        var pk = (PrimKind)br.ReadByte();
        return pk switch
        {
            PrimKind.Bool => br.ReadBoolean(),
            PrimKind.Byte => br.ReadByte(),
            PrimKind.SByte => br.ReadSByte(),
            PrimKind.Int16 => br.ReadInt16(),
            PrimKind.UInt16 => br.ReadUInt16(),
            PrimKind.Int32 => br.ReadInt32(),
            PrimKind.UInt32 => br.ReadUInt32(),
            PrimKind.Int64 => br.ReadInt64(),
            PrimKind.UInt64 => br.ReadUInt64(),
            PrimKind.Single => br.ReadSingle(),
            PrimKind.Double => br.ReadDouble(),
            PrimKind.Decimal => br.ReadDecimal(),
            PrimKind.String => br.ReadString(),
            PrimKind.Guid => new Guid(br.ReadBytes(16)),
            _ => throw new InvalidDataException($"Unknown primitive kind: {pk}")
        };
    }
}
