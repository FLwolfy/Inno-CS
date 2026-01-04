using System;
using Inno.Core.Resource;
using Inno.Graphics.Resources;

namespace Inno.Graphics.ResourceDecoders;

public class TextureDecoder : ResourceDecoder<Texture>
{
    protected override Texture OnDecode(byte[] bytes, string fullName)
    {
        // TODO
        throw new NotImplementedException();
    }
}