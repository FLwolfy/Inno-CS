using System;
using System.IO;
using System.Security.Cryptography;
using Inno.Assets.Serializer;

namespace Inno.Assets.AssetType;

public abstract class InnoAsset
{
    [AssetProperty] private string type { get; set; }
    [AssetProperty] public Guid guid { get; internal set; } = Guid.Empty;
    [AssetProperty] public string sourceHash { get; private set; } = string.Empty;
    [AssetProperty] public string sourcePath { get; internal set; } = string.Empty;

    public string name => Path.GetFileName(sourcePath);
    public byte[] assetBinaries { get; internal set; } = [];
    
    protected InnoAsset()
    {
        type = GetType().Name;
    }
    
    internal void RecomputeHash(string path)
    {
        using var stream = File.OpenRead(Path.Combine(AssetManager.assetDirectory, sourcePath));
        using var sha = SHA256.Create();
        var hashBytes = sha.ComputeHash(stream);
        sourceHash = Convert.ToHexString(hashBytes);
    }

    internal void RecomputeHash(byte[] bytes)
    {
        var hashBytes = SHA256.HashData(bytes);
        sourceHash = Convert.ToHexString(hashBytes);
    }
}