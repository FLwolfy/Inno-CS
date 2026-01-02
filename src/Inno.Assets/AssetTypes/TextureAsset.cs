using Inno.Assets.Serializers;
using Inno.Platform.Graphics;

namespace Inno.Assets.AssetTypes;

public sealed class TextureAsset : InnoAsset
{
    [AssetProperty] public int width { get; private set; }
    [AssetProperty] public int height { get; private set; }

    [AssetProperty] public PixelFormat format { get; private set; } = PixelFormat.R8_G8_B8_A8_UNorm;
    [AssetProperty] public TextureUsage usage { get; private set; } = TextureUsage.Sampled;
    [AssetProperty] public TextureDimension dimension { get; private set; } = TextureDimension.Texture2D;

    public byte[] pixelBytes { get; private set; } = [];

    internal TextureAsset(Guid guid, string sourcePath, int width, int height) : base(guid, sourcePath)
    {
        this.width = width;
        this.height = height;
    }

    internal override void OnBinaryLoaded(byte[] data) => pixelBytes = data;
}