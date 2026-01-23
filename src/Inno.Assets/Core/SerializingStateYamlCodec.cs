using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

using Inno.Core.Serialization;

namespace Inno.Assets.Core;

/// <summary>
/// Lossless YAML codec for SerializingState.
/// Guarantees: YAML -> SerializingState reconstructs the same state tree shape,
/// and preserves CLR primitive leaf types by storing ($kind,$type,$value) for each leaf.
/// Does NOT require any changes to SerializingState.
/// </summary>
internal static class SerializingStateYamlCodec
{
    // tagged node keys
    private const string K_KIND  = "$kind";
    private const string K_TYPE  = "$type";
    private const string K_VALUE = "$value";
    private const string K_ITEMS = "$items";
    private const string K_FIELDS = "$fields";

    // node kinds
    private const string KIND_NULL  = "null";
    private const string KIND_PRIM  = "prim";
    private const string KIND_ENUM  = "enum";
    private const string KIND_LIST  = "list";
    private const string KIND_STATE = "state";
    private const string KIND_STRUCT = "struct";
    private const string KIND_WRAPPER = "serializableWrapper"; // your in-memory wrapper: { "__type", "data": SerializingState }

    // ------------------------------------------------------------
    // Public API (tree-level)
    // ------------------------------------------------------------

    public static object EncodeState(SerializingState state)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [K_KIND]  = KIND_STATE,
            [K_VALUE] = EncodeMap(state.values),
        };
    }

    public static SerializingState DecodeState(object yamlRoot)
    {
        yamlRoot = NormalizeYamlObject(yamlRoot)
                   ?? throw new InvalidOperationException("YAML root is null.");

        if (yamlRoot is not Dictionary<string, object?> root)
            throw new InvalidOperationException("State YAML root must be a mapping.");

        if (!TryGetString(root, K_KIND, out var kind) || kind != KIND_STATE)
            throw new InvalidOperationException("State YAML root must be a { $kind: state } node.");

        if (!root.TryGetValue(K_VALUE, out var mapObj) || mapObj is not Dictionary<string, object?> map)
            throw new InvalidOperationException("State node missing $value mapping.");

        return new SerializingState(DecodeMap(map));
    }

    // ------------------------------------------------------------
    // Normalization (YamlDotNet outputs Dictionary<object,object> etc.)
    // ------------------------------------------------------------

    public static object? NormalizeYamlObject(object? node)
    {
        if (node == null) return null;

        if (node is IDictionary dict)
        {
            var m = new Dictionary<string, object?>(dict.Count, StringComparer.Ordinal);
            foreach (DictionaryEntry e in dict)
            {
                var key = e.Key as string ?? e.Key?.ToString() ?? string.Empty;
                if (key.Length == 0) continue;
                m[key] = NormalizeYamlObject(e.Value);
            }
            return m;
        }

        if (node is IList list && node is not string)
        {
            var l = new List<object?>(list.Count);
            for (int i = 0; i < list.Count; i++)
                l.Add(NormalizeYamlObject(list[i]));
            return l;
        }

        return node;
    }

    // ------------------------------------------------------------
    // Encode / Decode nodes
    // ------------------------------------------------------------

    private static Dictionary<string, object?> EncodeMap(IReadOnlyDictionary<string, object?> map)
    {
        var outMap = new Dictionary<string, object?>(map.Count, StringComparer.Ordinal);
        foreach (var kv in map)
            outMap[kv.Key] = EncodeNode(kv.Value);
        return outMap;
    }

    private static Dictionary<string, object?> DecodeMap(Dictionary<string, object?> map)
    {
        var outMap = new Dictionary<string, object?>(map.Count, StringComparer.Ordinal);
        foreach (var kv in map)
            outMap[kv.Key] = DecodeNode(kv.Value);
        return outMap;
    }

    private static object EncodeNode(object? v)
    {
        if (v == null)
            return new Dictionary<string, object?>(StringComparer.Ordinal) { [K_KIND] = KIND_NULL };

        if (v is SerializingState ss)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [K_KIND]  = KIND_STATE,
                [K_VALUE] = EncodeMap(ss.values),
            };
        }

        var t = v.GetType();

        // enum: preserve runtime enum type + int64
        if (t.IsEnum)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [K_KIND]  = KIND_ENUM,
                [K_TYPE]  = t.AssemblyQualifiedName ?? t.FullName ?? t.Name,
                [K_VALUE] = Convert.ToInt64(v, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            };
        }

        // primitives: preserve exact CLR primitive type
        if (IsAllowedPrimitive(t))
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [K_KIND]  = KIND_PRIM,
                [K_TYPE]  = t.AssemblyQualifiedName ?? t.FullName ?? t.Name,
                [K_VALUE] = EncodePrimitiveToString(v, t),
            };
        }

        // list: SerializingState containers are List<object?>
        if (v is List<object?> list)
        {
            var items = new List<object?>(list.Count);
            for (int i = 0; i < list.Count; i++)
                items.Add(EncodeNode(list[i]));

            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [K_KIND]  = KIND_LIST,
                [K_ITEMS] = items,
            };
        }

        // wrapper shape for ISerializable in your current in-memory state graph:
        // { "__type": "...", "data": SerializingState }
        if (v is Dictionary<string, object?> wrap
            && wrap.TryGetValue("__type", out var typeObj) && typeObj is string typeStr
            && wrap.TryGetValue("data", out var dataObj) && dataObj is SerializingState dataState)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [K_KIND]  = KIND_WRAPPER,
                [K_TYPE]  = typeStr,
                [K_VALUE] = EncodeState(dataState),
            };
        }

        // struct boxed: preserve runtime type + public members (F:/P:) recursively
        if (t.IsValueType && !t.IsEnum)
        {
            var fields = new Dictionary<string, object?>(StringComparer.Ordinal);

            foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public))
                fields["F:" + f.Name] = EncodeNode(f.GetValue(v));

            foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!p.CanRead) continue;
                if (p.GetIndexParameters().Length != 0) continue;
                if (!p.CanWrite) continue; // keep symmetric with your struct selection rules
                fields["P:" + p.Name] = EncodeNode(p.GetValue(v));
            }

            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [K_KIND]   = KIND_STRUCT,
                [K_TYPE]   = t.AssemblyQualifiedName ?? t.FullName ?? t.Name,
                [K_FIELDS] = fields,
            };
        }

        throw new InvalidOperationException($"SerializingState YAML cannot encode node type: {t.FullName}");
    }

    private static object? DecodeNode(object? node)
    {
        node = NormalizeYamlObject(node);

        if (node == null) return null;

        if (node is not Dictionary<string, object?> m
            || !TryGetString(m, K_KIND, out var kind))
            throw new InvalidOperationException("Invalid state YAML node (expected tagged mapping with $kind).");

        switch (kind)
        {
            case KIND_NULL:
                return null;

            case KIND_PRIM:
            {
                if (!TryGetString(m, K_TYPE, out var typeStr))
                    throw new InvalidOperationException("prim node missing $type.");
                if (!TryGetString(m, K_VALUE, out var valStr))
                    throw new InvalidOperationException("prim node missing $value.");

                var t = Type.GetType(typeStr) ?? throw new InvalidOperationException($"Cannot resolve prim type: {typeStr}");
                return DecodePrimitiveFromString(valStr, t);
            }

            case KIND_ENUM:
            {
                if (!TryGetString(m, K_TYPE, out var typeStr))
                    throw new InvalidOperationException("enum node missing $type.");
                if (!TryGetString(m, K_VALUE, out var valStr))
                    throw new InvalidOperationException("enum node missing $value.");

                var enumType = Type.GetType(typeStr) ?? throw new InvalidOperationException($"Cannot resolve enum type: {typeStr}");
                var n = long.Parse(valStr, CultureInfo.InvariantCulture);
                return Enum.ToObject(enumType, n);
            }

            case KIND_LIST:
            {
                if (!m.TryGetValue(K_ITEMS, out var itemsObj) || itemsObj is not List<object?> items)
                    throw new InvalidOperationException("list node missing $items.");

                var list = new List<object?>(items.Count);
                for (int i = 0; i < items.Count; i++)
                    list.Add(DecodeNode(items[i]));
                return list;
            }

            case KIND_STATE:
            {
                if (!m.TryGetValue(K_VALUE, out var mapObj) || mapObj is not Dictionary<string, object?> map)
                    throw new InvalidOperationException("state node missing $value mapping.");

                return new SerializingState(DecodeMap(map));
            }

            case KIND_WRAPPER:
            {
                if (!TryGetString(m, K_TYPE, out var typeStr))
                    throw new InvalidOperationException("wrapper node missing $type.");
                if (!m.TryGetValue(K_VALUE, out var stObj))
                    throw new InvalidOperationException("wrapper node missing $value (state).");

                var decoded = DecodeNode(stObj);
                if (decoded is not SerializingState ss)
                    throw new InvalidOperationException("wrapper $value must decode to SerializingState.");

                // Rebuild the SAME wrapper shape used in your state graph
                return new Dictionary<string, object?>(2, StringComparer.Ordinal)
                {
                    ["__type"] = typeStr,
                    ["data"] = ss
                };
            }

            case KIND_STRUCT:
            {
                if (!TryGetString(m, K_TYPE, out var typeStr))
                    throw new InvalidOperationException("struct node missing $type.");
                if (!m.TryGetValue(K_FIELDS, out var fieldsObj) || fieldsObj is not Dictionary<string, object?> fields)
                    throw new InvalidOperationException("struct node missing $fields.");

                var t = Type.GetType(typeStr) ?? throw new InvalidOperationException($"Cannot resolve struct type: {typeStr}");
                if (!t.IsValueType || t.IsEnum)
                    throw new InvalidOperationException($"struct $type is not a struct: {t.FullName}");

                object boxed = Activator.CreateInstance(t)!;

                foreach (var kv in fields)
                {
                    var key = kv.Key;
                    var decodedVal = DecodeNode(kv.Value);

                    if (key.StartsWith("F:", StringComparison.Ordinal))
                    {
                        var name = key.Substring(2);
                        var fi = t.GetField(name, BindingFlags.Instance | BindingFlags.Public);
                        if (fi != null) fi.SetValue(boxed, decodedVal);
                        continue;
                    }

                    if (key.StartsWith("P:", StringComparison.Ordinal))
                    {
                        var name = key.Substring(2);
                        var pi = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
                        if (pi != null && pi.CanWrite) pi.SetValue(boxed, decodedVal);
                        continue;
                    }
                }

                return boxed;
            }

            default:
                throw new InvalidOperationException($"Unknown state yaml node kind: {kind}");
        }
    }

    // ------------------------------------------------------------
    // Primitive helpers
    // ------------------------------------------------------------

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

    private static string EncodePrimitiveToString(object v, Type t)
    {
        if (t == typeof(string)) return (string)v;
        if (t == typeof(Guid)) return ((Guid)v).ToString("D");
        if (t == typeof(bool)) return ((bool)v) ? "true" : "false";

        if (t == typeof(byte) || t == typeof(sbyte) ||
            t == typeof(short) || t == typeof(ushort) ||
            t == typeof(int) || t == typeof(uint) ||
            t == typeof(long) || t == typeof(ulong))
            return Convert.ToString(v, CultureInfo.InvariantCulture) ?? "0";

        if (t == typeof(float))  return ((float)v).ToString("R", CultureInfo.InvariantCulture);
        if (t == typeof(double)) return ((double)v).ToString("R", CultureInfo.InvariantCulture);

        if (t == typeof(decimal)) return ((decimal)v).ToString(CultureInfo.InvariantCulture);

        throw new InvalidOperationException($"Unsupported primitive type: {t.FullName}");
    }

    private static object DecodePrimitiveFromString(string s, Type t)
    {
        if (t == typeof(string)) return s;
        if (t == typeof(Guid)) return Guid.Parse(s);
        if (t == typeof(bool)) return string.Equals(s, "true", StringComparison.OrdinalIgnoreCase);

        if (t == typeof(byte)) return byte.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (t == typeof(sbyte)) return sbyte.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (t == typeof(short)) return short.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (t == typeof(ushort)) return ushort.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (t == typeof(int)) return int.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (t == typeof(uint)) return uint.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (t == typeof(long)) return long.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (t == typeof(ulong)) return ulong.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);

        if (t == typeof(float)) return float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
        if (t == typeof(double)) return double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
        if (t == typeof(decimal)) return decimal.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

        throw new InvalidOperationException($"Unsupported primitive type: {t.FullName}");
    }

    private static bool TryGetString(Dictionary<string, object?> m, string key, out string value)
    {
        value = string.Empty;
        if (!m.TryGetValue(key, out var obj) || obj is not string s) return false;
        value = s;
        return true;
    }
}
