using Inno.Assets.AssetType;
using Inno.Graphics.Resources.CpuResources;

namespace Inno.Graphics.Decoder;

internal class TextureDecoder : ResourceDecoder<Texture, TextureAsset>
{
    protected override Texture OnDecode(TextureAsset asset)
    {
        return new Texture(
            asset.guid,
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