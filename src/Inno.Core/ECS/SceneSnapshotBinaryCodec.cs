using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Inno.Core.Serialization;

namespace Inno.Core.ECS;

/// <summary>
/// Deterministic, type-preserving binary codec for <see cref="SceneSnapshot.SceneSnapshotData"/>.
///
/// Rationale:
/// - ISerializable.CaptureState now produces a <see cref="SerializedState"/> which wraps a map of boxed values
///   (engine value-types like Vector2/3/Quaternion/Matrix, etc.) and AssetRef&lt;T&gt;.
/// - Text formats (YAML/JSON) commonly round-trip those boxed structs into generic dictionaries, losing runtime types.
/// - ISerializable.RestoreState expects boxed values of the original runtime types for value-types.
///
/// This codec stores runtime types for all values, enabling stable SceneAsset persistence.
/// </summary>
public static class SceneSnapshotBinaryCodec
{
    // NOTE: Keep header short but unique; ASCII only.
    private static readonly byte[] MAGIC = "INNO_SCENE"u8.ToArray();

    // Version bump because we introduced a dedicated node kind for SerializedState.
    private const int C_VERSION = 2;

    private enum NodeKind : byte
    {
        Null = 0,
        Bool = 1,
        Int32 = 2,
        Int64 = 3,
        Float = 4,
        Double = 5,
        Decimal = 6,
        String = 7,
        Guid = 8,

        Map = 20,       // Dictionary<string, object?>
        StateMap = 21,  // SerializedState (wraps IReadOnlyDictionary<string, object?>)

        Struct = 30,
        AssetRef = 31,
    }

    public static byte[] Serialize(SceneSnapshot.SceneSnapshotData snapshot)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

        using var ms = new MemoryStream(16 * 1024);
        using var bw = new BinaryWriter(ms);

        // Header
        bw.Write(MAGIC);
        bw.Write(C_VERSION);

        bw.Write(snapshot.sceneName ?? string.Empty);
        bw.Write(snapshot.objects?.Count ?? 0);

        if (snapshot.objects != null)
        {
            for (int i = 0; i < snapshot.objects.Count; i++)
            {
                var o = snapshot.objects[i];
                bw.Write(o.name ?? string.Empty);

                var hasParent = !string.IsNullOrEmpty(o.parentName);
                bw.Write(hasParent);
                if (hasParent) bw.Write(o.parentName!);

                bw.Write(o.components?.Count ?? 0);
                if (o.components == null) continue;

                for (int c = 0; c < o.components.Count; c++)
                {
                    var comp = o.components[c];
                    bw.Write(comp.type ?? string.Empty);

                    // Component state is a SerializedState now.
                    WriteState(bw, comp.state);
                }
            }
        }

        bw.Flush();
        return ms.ToArray();
    }

    public static SceneSnapshot.SceneSnapshotData Deserialize(byte[] bytes)
    {
        if (bytes == null) throw new ArgumentNullException(nameof(bytes));
        if (bytes.Length == 0) throw new InvalidDataException("Scene data is empty.");

        using var ms = new MemoryStream(bytes, writable: false);
        using var br = new BinaryReader(ms);

        // Header
        var magic = br.ReadBytes(MAGIC.Length);
        if (!magic.SequenceEqual(MAGIC))
            throw new InvalidDataException("Not a valid INNO scene file (missing magic header).");

        int version = br.ReadInt32();
        if (version != C_VERSION)
            throw new InvalidDataException($"Unsupported INNO scene version: {version}. Expected {C_VERSION}.");

        var sceneName = br.ReadString();
        int objCount = br.ReadInt32();

        var objects = new List<SceneSnapshot.GameObjectSnapshotData>(System.Math.Max(0, objCount));
        for (int i = 0; i < objCount; i++)
        {
            var name = br.ReadString();

            string? parent = null;
            bool hasParent = br.ReadBoolean();
            if (hasParent) parent = br.ReadString();

            int compCount = br.ReadInt32();
            var comps = new List<SceneSnapshot.ComponentSnapshotData>(System.Math.Max(0, compCount));
            for (int c = 0; c < compCount; c++)
            {
                var type = br.ReadString();
                var state = ReadState(br);

                comps.Add(new SceneSnapshot.ComponentSnapshotData
                {
                    type = type,
                    state = state
                });
            }

            objects.Add(new SceneSnapshot.GameObjectSnapshotData
            {
                name = name,
                parentName = parent,
                components = comps
            });
        }

        return new SceneSnapshot.SceneSnapshotData
        {
            sceneName = sceneName,
            objects = objects
        };
    }

    // ----------------------------
    // SerializedState support
    // ----------------------------

    private static void WriteState(BinaryWriter bw, SerializedState state)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));

        bw.Write((byte)NodeKind.StateMap);

        // Deterministic: sort keys.
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

    private static SerializedState ReadState(BinaryReader br)
    {
        var kind = (NodeKind)br.ReadByte();
        if (kind != NodeKind.StateMap)
            throw new InvalidDataException($"Expected SerializedState node, got {kind}.");

        int count = br.ReadInt32();
        var map = new Dictionary<string, object?>(System.Math.Max(0, count), StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            var key = br.ReadString();
            var val = ReadNode(br);
            map[key] = val;
        }

        return new SerializedState(map);
    }

    // ----------------------------
    // Dictionary (wrapper map) support
    // ----------------------------

    private static void WriteMap(BinaryWriter bw, IReadOnlyDictionary<string, object?> map)
    {
        bw.Write((byte)NodeKind.Map);

        // Deterministic: sort keys.
        var keys = map.Keys.ToList();
        keys.Sort(StringComparer.Ordinal);

        bw.Write(keys.Count);
        for (int i = 0; i < keys.Count; i++)
        {
            var k = keys[i];
            bw.Write(k);
            map.TryGetValue(k, out var v);
            WriteNode(bw, v);
        }
    }

    private static Dictionary<string, object?> ReadMap(BinaryReader br)
    {
        var kind = (NodeKind)br.ReadByte();
        if (kind != NodeKind.Map)
            throw new InvalidDataException($"Expected map node, got {kind}.");

        int count = br.ReadInt32();
        var map = new Dictionary<string, object?>(System.Math.Max(0, count), StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            var key = br.ReadString();
            var val = ReadNode(br);
            map[key] = val;
        }

        return map;
    }

    private static void WriteNode(BinaryWriter bw, object? value)
    {
        if (value == null)
        {
            bw.Write((byte)NodeKind.Null);
            return;
        }

        var t = value.GetType();

        if (t == typeof(bool)) { bw.Write((byte)NodeKind.Bool); bw.Write((bool)value); return; }
        if (t == typeof(int)) { bw.Write((byte)NodeKind.Int32); bw.Write((int)value); return; }
        if (t == typeof(long)) { bw.Write((byte)NodeKind.Int64); bw.Write((long)value); return; }
        if (t == typeof(float)) { bw.Write((byte)NodeKind.Float); bw.Write((float)value); return; }
        if (t == typeof(double)) { bw.Write((byte)NodeKind.Double); bw.Write((double)value); return; }
        if (t == typeof(decimal)) { bw.Write((byte)NodeKind.Decimal); bw.Write((decimal)value); return; }
        if (t == typeof(string)) { bw.Write((byte)NodeKind.String); bw.Write((string)value); return; }
        if (t == typeof(Guid)) { bw.Write((byte)NodeKind.Guid); bw.Write(((Guid)value).ToByteArray()); return; }

        // NEW: SerializedState nodes (used by nested Serializable wrappers: { "__type": "...", "data": SerializedState })
        if (value is SerializedState ss)
        {
            WriteState(bw, ss);
            return;
        }

        // Nested Serializable wrapper nodes are emitted as Dictionary<string, object?>.
        if (value is Dictionary<string, object?> dict)
        {
            WriteMap(bw, dict);
            return;
        }

        if (t.IsValueType)
        {
            bw.Write((byte)NodeKind.Struct);
            bw.Write(t.AssemblyQualifiedName ?? t.FullName ?? t.Name);

            var fields = GetSerializableStructFields(t);
            bw.Write(fields.Length);
            for (int i = 0; i < fields.Length; i++)
            {
                var f = fields[i];
                bw.Write(f.Name);
                WriteNode(bw, f.GetValue(value));
            }
            return;
        }

        throw new InvalidDataException($"Unsupported scene node type: {t.FullName}");
    }

    private static object? ReadNode(BinaryReader br)
    {
        var kind = (NodeKind)br.ReadByte();
        return kind switch
        {
            NodeKind.Null => null,
            NodeKind.Bool => br.ReadBoolean(),
            NodeKind.Int32 => br.ReadInt32(),
            NodeKind.Int64 => br.ReadInt64(),
            NodeKind.Float => br.ReadSingle(),
            NodeKind.Double => br.ReadDouble(),
            NodeKind.Decimal => br.ReadDecimal(),
            NodeKind.String => br.ReadString(),
            NodeKind.Guid => new Guid(br.ReadBytes(16)),

            NodeKind.Map => ReadMapAfterKind(br),
            NodeKind.StateMap => ReadStateAfterKind(br),

            NodeKind.Struct => ReadStruct(br),

            _ => throw new InvalidDataException($"Unknown node kind: {kind}")
        };
    }

    private static Dictionary<string, object?> ReadMapAfterKind(BinaryReader br)
    {
        int count = br.ReadInt32();
        var map = new Dictionary<string, object?>(System.Math.Max(0, count), StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            var key = br.ReadString();
            var val = ReadNode(br);
            map[key] = val;
        }
        return map;
    }

    private static SerializedState ReadStateAfterKind(BinaryReader br)
    {
        int count = br.ReadInt32();
        var map = new Dictionary<string, object?>(System.Math.Max(0, count), StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            var key = br.ReadString();
            var val = ReadNode(br);
            map[key] = val;
        }
        return new SerializedState(map);
    }

    private static object ReadStruct(BinaryReader br)
    {
        string typeName = br.ReadString();
        var t = Type.GetType(typeName);
        if (t == null)
            throw new InvalidDataException($"Could not resolve struct type '{typeName}'.");

        object boxed = Activator.CreateInstance(t)!;

        int fieldCount = br.ReadInt32();
        var fieldMap = new Dictionary<string, FieldInfo>(fieldCount, StringComparer.Ordinal);
        foreach (var f in GetSerializableStructFields(t))
            fieldMap[f.Name] = f;

        for (int i = 0; i < fieldCount; i++)
        {
            string fieldName = br.ReadString();
            var v = ReadNode(br);
            if (fieldMap.TryGetValue(fieldName, out var fi))
            {
                fi.SetValue(boxed, v);
            }
        }

        return boxed;
    }
    
    private static FieldInfo[] GetSerializableStructFields(Type t)
    {
        return t.GetFields(BindingFlags.Instance | BindingFlags.Public)
            .OrderBy(f => f.MetadataToken)
            .ToArray();
    }
}
