namespace Inno.Core.Resource;

internal interface IResourceDecoder
{
    object Decode(ResourceBin bin);
}

/// <summary>
/// The generic class for the resource decoder.
/// </summary>
public abstract class ResourceDecoder<T> : IResourceDecoder where T : notnull
{
    object IResourceDecoder.Decode(ResourceBin bin) => OnDecode(bin);
   
    /// <summary>
    /// The method to decode binaries into given type.
    /// </summary>
    protected abstract T OnDecode(ResourceBin bin);
}