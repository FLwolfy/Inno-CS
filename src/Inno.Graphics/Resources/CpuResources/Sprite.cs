using Inno.Assets;
using Inno.Assets.AssetType;
using Inno.Assets.Core;
using Inno.Core.Math;
using Inno.Core.Serialization;
using Inno.Graphics.Decoder;

namespace Inno.Graphics.Resources.CpuResources;

public sealed class Sprite : ISerializable
{
    public Texture? texture { get; private set; }

    [SerializableProperty] private AssetRef<TextureAsset>? m_textureAsset;
    [SerializableProperty] public Vector4 uv;
    [SerializableProperty] public Vector2 size;

    private Sprite(Texture? texture, Vector4 uv, Vector2 size)
    {
        this.texture = texture;
        if (texture != null)
        {
            m_textureAsset = AssetManager.Get<TextureAsset>(texture.guid);
        }
        
        this.uv = uv;
        this.size = size;
    }

    public static Sprite FromTexture(Texture texture)
    {
        var sprite = new Sprite(
            texture,
            new Vector4(0, 0, 1, 1),
            new Vector2(texture.width, texture.height)
        );
        
        return sprite;
    }

    public static Sprite SolidColor(Vector2 size)
    {
        return new Sprite(
            null,
            new Vector4(0, 0, 1, 1),
            size
        );
    }

    [OnSerializableRestored]
    private void OnAfterRestore()
    {
        if (m_textureAsset != null)
        {
            var asset = m_textureAsset.Value.Resolve();
            if (asset != null)
            {
                texture = ResourceDecoder.DecodeBinaries<Texture, TextureAsset>(asset);
            }
        }
    }
        
}