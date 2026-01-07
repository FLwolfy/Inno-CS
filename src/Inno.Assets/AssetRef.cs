using System;
using Inno.Assets.AssetType;

namespace Inno.Assets;

public readonly struct AssetRef<T> where T : InnoAsset
{
    public Guid guid { get; }
    internal string? embeddedKey { get; }

    internal AssetRef(Guid guid, string? embeddedKey)
    {
        this.guid = guid; 
        this.embeddedKey = embeddedKey;
    }

    public bool isEmbedded => embeddedKey != null;
    public bool isValid => guid != Guid.Empty || isEmbedded;

    public T? Resolve() => AssetManager.ResolveAssetRef(this);

    public override string ToString()
    {
        if (!isValid) 
            return $"{typeof(T).Name}: Invalid";
        
        if (isEmbedded) 
            return $"{typeof(T).Name}: (Embedded Key) {embeddedKey}";
        
        return $"{typeof(T).Name}: {guid}";
    }
}