namespace Inno.Core.Resources;

public interface IResourceDecoder
{
    object Decode(byte[] bytes, string fullName);
}

/// <summary>
/// The abstract class for the resource decoder.
/// </summary>
public abstract class ResourceDecoder<T> : IResourceDecoder where T : notnull
{
    object IResourceDecoder.Decode(byte[] bytes, string fullName) => this.OnDecode(bytes, fullName);
   
    /// <summary>
    /// The method to decode bytes into given type.
    /// </summary>
    /// <param name="bytes"></param>
    /// <param name="fullName"></param>
    /// <returns></returns>
    protected abstract T OnDecode(byte[] bytes, string fullName);
}