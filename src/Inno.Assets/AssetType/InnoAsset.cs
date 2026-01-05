using System;
using System.IO;
using System.Security.Cryptography;
using Inno.Assets.Serializer;
using Inno.Core.Resource;

namespace Inno.Assets.AssetType;

public abstract class InnoAsset
{
    [AssetProperty] private string type { get; set; }
    [AssetProperty] public Guid guid { get; internal set; } = Guid.Empty;
    [AssetProperty] public string sourceHash { get; private set; } = string.Empty;
    [AssetProperty] public string sourcePath { get; internal set; } = string.Empty;
    
    protected internal ResourceBin assetBinaries { get; internal set; }
    
    protected InnoAsset()
    {
        type = GetType().Name;
    }
    
    internal void RecomputeHash()
    {
        using var stream = File.OpenRead(Path.Combine(AssetManager.assetDirectory, sourcePath));
        using var sha = SHA256.Create();
        var hashBytes = sha.ComputeHash(stream);
        sourceHash = Convert.ToHexString(hashBytes);
    }
}