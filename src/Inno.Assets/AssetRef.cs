using System;
using System.Security.Cryptography;
using System.Text;
using Inno.Assets.AssetType;

namespace Inno.Assets;

public readonly struct AssetRef<T> where T : InnoAsset
{
    public Guid guid { get; }
    public bool isEmbedded { get; }

    internal AssetRef(Guid guid, bool isEmbedded)
    {
        this.guid = guid; 
        this.isEmbedded = isEmbedded;
    }

    public bool isValid => guid != Guid.Empty;

    public T? Resolve() => AssetManager.ResolveAssetRef(this);

    public override string ToString()
    {
        if (!isValid) 
            return $"{typeof(T).Name}: Invalid";
        
        if (isEmbedded) 
            return $"{typeof(T).Name}: (Embedded) {guid}";
        
        return $"{typeof(T).Name}: {guid}";
    }
}