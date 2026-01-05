using System;
using System.IO;
using Inno.Assets.AssetType;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Inno.Assets.Loader;

internal sealed class TextureAssetLoader : InnoAssetLoader<TextureAsset>
{
    public override string[] validExtensions => [".png"];

    protected override TextureAsset OnLoad(string relativePath)
    {
        // Based on Platform PixelFormat.R8_G8_B8_A8_UNorm
        string absPath = Path.Combine(AssetManager.assetDirectory, relativePath);
        using var img = Image.Load<Rgba32>(absPath);

        return new TextureAsset(
            img.Width,
            img.Height
        );
    }

    protected override byte[] OnBinarize(string relativePath)
    {
        string absPath = Path.Combine(AssetManager.assetDirectory, relativePath);
        using var img = Image.Load<Rgba32>(absPath);

        byte[] bytes = new byte[img.Width * img.Height * 4];
        img.CopyPixelDataTo(bytes);

        return bytes;
    }
}