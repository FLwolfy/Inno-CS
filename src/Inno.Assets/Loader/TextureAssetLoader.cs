using Inno.Assets.AssetType;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Inno.Assets.Loader;

internal sealed class TextureAssetLoader : InnoAssetLoader<TextureAsset>
{
    public override string[] validExtensions => [".png"];

    protected override byte[] OnLoadBinaries(string assetName, byte[] rawBytes, out TextureAsset asset)
    {
        using var img = Image.Load<Rgba32>(rawBytes);

        byte[] pixels = new byte[img.Width * img.Height * 4];
        img.CopyPixelDataTo(pixels);

        asset = new TextureAsset(img.Width, img.Height);

        return pixels;
    }
}