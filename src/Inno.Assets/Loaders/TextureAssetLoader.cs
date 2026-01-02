using Inno.Assets.AssetTypes;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Inno.Assets.Loaders;

internal sealed class TextureAssetLoader : InnoAssetLoader<TextureAsset>
{
    public override string[] validExtensions => [".png"];

    protected override TextureAsset OnLoad(string relativePath, Guid guid)
    {
        string absPath = Path.Combine(AssetManager.assetDirectory, relativePath);

        // Based on Platform PixelFormat.R8_G8_B8_A8_UNorm
        using var img = Image.Load<Rgba32>(absPath);

        var t = new TextureAsset(
            guid,
            relativePath,
            img.Width,
            img.Height
        );
        
        return t;
    }

    protected override byte[]? OnCompile(string relativePath)
    {
        string absPath = Path.Combine(AssetManager.assetDirectory, relativePath);

        using var img = Image.Load<Bgra32>(absPath);

        byte[] bytes = new byte[img.Width * img.Height * 4];
        img.CopyPixelDataTo(bytes);

        return bytes;
    }
}