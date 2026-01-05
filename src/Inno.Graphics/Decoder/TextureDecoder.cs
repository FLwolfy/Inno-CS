using Inno.Assets.AssetType;
using Inno.Graphics.Resources;

namespace Inno.Graphics.Decoder;

public class TextureDecoder : ResourceDecoder<Texture, TextureAsset>
{
    protected override Texture OnDecode(TextureAsset asset)
    {
        return new Texture
        (
            asset.name,
            asset.width,
            asset.height,
            asset.assetBinaries,
            asset.format,
            asset.usage,
            asset.dimension
        );
    }
}