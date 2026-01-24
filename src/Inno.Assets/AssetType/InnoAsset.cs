using System;
using System.IO;
using System.Security.Cryptography;

using Inno.Core.Serialization;

namespace Inno.Assets.AssetType;

public abstract class InnoAsset : ISerializable
{
    [SerializableProperty] private string type { get; set; }
    [SerializableProperty] public Guid guid { get; internal set; }
    [SerializableProperty] internal string sourceHash { get; private set; } = string.Empty;
    [SerializableProperty] internal string sourcePath { get; private set; } = string.Empty;

    public string name => Path.GetFileName(sourcePath);
    public byte[] assetBinaries { get; internal set; } = [];
    
    protected InnoAsset()
    {
        type = GetType().Name;
        guid = Guid.NewGuid();
    }
    
    internal void RecomputeHash(string relativePath)
    {
        using var stream = File.OpenRead(Path.Combine(AssetManager.assetDirectory, relativePath));
        using var sha = SHA256.Create();
        var hashBytes = sha.ComputeHash(stream);
        sourceHash = Convert.ToHexString(hashBytes);
    }

    internal void RecomputeHash(byte[] bytes)
    {
        var hashBytes = SHA256.HashData(bytes);
        sourceHash = Convert.ToHexString(hashBytes);
    }

    internal void SetSourcePath(string relativePath)
    {
        sourcePath = relativePath;
    }
}