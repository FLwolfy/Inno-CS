using System.Security.Cryptography;
using Inno.Assets.Serializers;

namespace Inno.Assets.AssetTypes;

public abstract class InnoAsset
{
    [AssetProperty] private string type { get; set; }
    [AssetProperty] public Guid guid { get; private set; }
    [AssetProperty] public string sourceHash { get; private set; } = string.Empty;
    [AssetProperty] public string sourcePath { get; private set; }
    
    protected internal InnoAsset(Guid guid, string sourcePath)
    {
        type = GetType().Name;
        
        this.guid = guid;
        this.sourcePath = sourcePath;
        RecomputeHash();
    }
    
    internal void RecomputeHash()
    {
        using var stream = File.OpenRead(Path.Combine(AssetManager.assetDirectory, sourcePath));
        using var sha = SHA256.Create();
        var hashBytes = sha.ComputeHash(stream);
        sourceHash = Convert.ToHexString(hashBytes);
    }

    internal virtual void OnBinaryLoaded(byte[] data) {}
}