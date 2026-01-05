using Inno.Platform.Graphics;

namespace Inno.Graphics.Resources;

public class Texture(
    string name,
    int width,
    int height,
    byte[] data,
    PixelFormat format,
    TextureUsage usage,
    TextureDimension dimension
) {
    public string name { get; } = name;
    public int width { get; } = width;
    public int height { get; } = height;
    public byte[] data { get; } = data;

    public PixelFormat format { get; } = format;
    public TextureUsage usage { get; } = usage;
    public TextureDimension dimension { get; } = dimension;
}