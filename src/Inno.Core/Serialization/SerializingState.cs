using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Inno.Core.Serialization;

public sealed record SerializingState
{
    private const string C_INNO_MAGIC_HEADER = "INNO";
    private const int C_SERIALIZATION_VERSION = 1;
    
    public IReadOnlyDictionary<string, object?> values { get; }

    public SerializingState(IReadOnlyDictionary<string, object?> values)
    {
        this.values = values ?? throw new ArgumentNullException(nameof(values));
    }

    public bool Contains(string key) => values.ContainsKey(key);

    public bool TryGetValue(string key, out object? value) => values.TryGetValue(key, out value);

    public T GetValue<T>(string key)
    {
        if (!values.TryGetValue(key, out var v))
            throw new KeyNotFoundException(key);

        if (v is T t) return t;

        throw new InvalidCastException(
            $"State value '{key}' is {v?.GetType().FullName}, expected {typeof(T).FullName}");
    }

    // =====================================================================
    //  Binary API lives here
    // =====================================================================

    /// <summary>
    /// Serializes this state into a deterministic binary format.
    /// </summary>
    public byte[] Serialize()
    {
        using var ms = new MemoryStream(16 * 1024);
        using var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

        bw.Write(C_INNO_MAGIC_HEADER);     // Magic
        bw.Write(C_SERIALIZATION_VERSION); // version

        ISerializable.WriteState(bw, this);

        bw.Flush();
        return ms.ToArray();
    }

    /// <summary>
    /// Deserializes bytes produced by <see cref="Serialize"/> back into a state.
    /// </summary>
    public static SerializingState Deserialize(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0) throw new InvalidDataException("Data is empty.");

        using var ms = new MemoryStream(bytes.ToArray(), writable: false);
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        var magic = br.ReadString();
        if (!string.Equals(magic, C_INNO_MAGIC_HEADER, StringComparison.Ordinal))
            throw new InvalidDataException("Not a valid INNO_MAGIC_HEADER stream.");

        var ver = br.ReadInt32();
        if (ver != 1)
            throw new InvalidDataException($"Unsupported SERIALIZATION_VERSION: {ver}.");

        // The stream should contain exactly one state node next.
        return ISerializable.ReadState(br);
    }
}
