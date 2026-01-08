using Inno.Core.Math;
using Inno.Graphics.Resources;

namespace Inno.Runtime.RenderObject;

public sealed class Sprite
{
    public Texture? texture { get; }
    public Vector4 uv;
    public Vector2 pivot;
    public Vector2 size;

    private Sprite(Texture? texture, Vector4 uv, Vector2 pivot, Vector2 size)
    {
        this.texture = texture;
        this.uv = uv;
        this.pivot = pivot;
        this.size = size;
    }

    public static Sprite FromTexture(Texture texture)
    {
        return new Sprite(
            texture,
            new Vector4(0, 0, 1, 1),
            new Vector2(0.5f, 0.5f),
            new Vector2(texture.width, texture.height)
        );
    }

    public static Sprite SolidColor(Vector2 size)
    {
        return new Sprite(
            null,
            new Vector4(0, 0, 1, 1),
            new Vector2(0.5f, 0.5f),
            size
        );
    }
}