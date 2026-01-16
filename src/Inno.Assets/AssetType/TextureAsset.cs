using Inno.Core.Serialization;
using Inno.Platform.Graphics;

namespace Inno.Assets.AssetType;

public sealed class TextureAsset : InnoAsset
{
    [SerializableProperty] public int width { get; private set; }
    [SerializableProperty] public int height { get; private set; }
    [SerializableProperty] public PixelFormat format { get; private set; } = PixelFormat.R8_G8_B8_A8_UNorm;
    [SerializableProperty] public TextureUsage usage { get; private set; } = TextureUsage.Sampled;
    [SerializableProperty] public TextureDimension dimension { get; private set; } = TextureDimension.Texture2D;

    internal TextureAsset(int width, int height)
    {
        this.width = width;
        this.height = height;
    }
}