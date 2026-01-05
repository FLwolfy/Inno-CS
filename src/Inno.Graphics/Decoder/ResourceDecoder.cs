using Inno.Assets.AssetType;

namespace Inno.Graphics.Decoder;

internal interface IResourceDecoder
{
    object Decode(InnoAsset asset);
}

/// <summary>
/// The static API class for the resource decoder.
/// </summary>
public static class ResourceDecoder
{
    public static T DecodeBinaries<T, TAsset>(TAsset asset) 
        where T : notnull
        where TAsset : InnoAsset
        => DecoderRegistry.Decode<T, TAsset>(asset);
}

/// <summary>
/// The generic class for the resource decoder.
/// </summary>
public abstract class ResourceDecoder<T, TAsset> : IResourceDecoder 
    where T : notnull
    where TAsset : InnoAsset
{
    object IResourceDecoder.Decode(InnoAsset asset) => OnDecode((TAsset)asset);
   
    /// <summary>
    /// The method to decode binaries into given type.
    /// </summary>
    protected abstract T OnDecode(TAsset asset);
}