using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Inno.Core.ECS;

/// <summary>
/// Deterministic, type-preserving binary codec for <see cref="SceneSnapshot.SceneSnapshotData"/>.
///
/// Rationale:
/// - <see cref="Inno.Core.Serialization.Serializable.CaptureState"/> produces a node of <c>Dictionary&lt;string, object?&gt;</c>
///   which contains boxed engine value-types (Vector2/3/Quaternion/Matrix, etc.) and AssetRef&lt;T&gt;.
/// - Text formats (YAML/JSON) commonly round-trip those boxed structs into generic dictionaries, losing runtime types.
/// - <see cref="Inno.Core.Serialization.Serializable.RestoreState"/> expects boxed values of the original runtime types for value-types.
///
/// This codec stores runtime types for all values, enabling stable SceneAsset persistence.
/// </summary>
public static class SceneSnapshotBinaryCodec
{
    // NOTE: Keep header short but unique; ASCII only.
    private static readonly byte[] MAGIC = "INNO_SCENE"u8.ToArray();
    private const int C_VERSION = 1;

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

        Map = 20,
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
                    WriteMap(bw, comp.state ?? new Dictionary<string, object?>(StringComparer.Ordinal));
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
                var state = ReadMap(br);
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

    private static void WriteMap(BinaryWriter bw, Dictionary<string, object?> map)
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

        // Nested Serializable wrapper nodes are emitted as Dictionary<string, object?>.
        if (value is Dictionary<string, object?> dict)
        {
            WriteMap(bw, dict);
            return;
        }

        if (t.IsValueType)
        {
            if (IsAssetRefType(t))
            {
                bw.Write((byte)NodeKind.AssetRef);
                bw.Write(t.AssemblyQualifiedName ?? t.FullName ?? t.Name);

                var guidProp = t.GetProperty("guid", BindingFlags.Instance | BindingFlags.Public);
                var embProp = t.GetProperty("isEmbedded", BindingFlags.Instance | BindingFlags.Public);

                var g = guidProp != null ? (Guid)guidProp.GetValue(value)! : Guid.Empty;
                var isEmb = embProp != null && (bool)embProp.GetValue(value)!;

                bw.Write(g.ToByteArray());
                bw.Write(isEmb);
                return;
            }

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
            NodeKind.Struct => ReadStruct(br),
            NodeKind.AssetRef => ReadAssetRef(br),
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

    private static object ReadAssetRef(BinaryReader br)
    {
        string typeName = br.ReadString();
        var t = Type.GetType(typeName);
        if (t == null)
            throw new InvalidDataException($"Could not resolve AssetRef type '{typeName}'.");

        var guid = new Guid(br.ReadBytes(16));
        bool isEmbedded = br.ReadBoolean();

        var ctor = t.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(c =>
            {
                var p = c.GetParameters();
                return p.Length == 2 && p[0].ParameterType == typeof(Guid) && p[1].ParameterType == typeof(bool);
            });

        if (ctor == null)
            throw new InvalidDataException($"Could not find AssetRef ctor on '{t.FullName}'.");

        return ctor.Invoke([guid, isEmbedded]);
    }

    private static bool IsAssetRefType(Type t)
        => t.IsGenericType && t.GetGenericTypeDefinition().FullName == "Inno.Assets.AssetRef`1";

    private static FieldInfo[] GetSerializableStructFields(Type t)
    {
        return t.GetFields(BindingFlags.Instance | BindingFlags.Public)
            .OrderBy(f => f.MetadataToken)
            .ToArray();
    }
}
