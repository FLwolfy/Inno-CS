using Inno.Core.ECS;
using Inno.Core.Math;
using Inno.Core.Serialization;
using Inno.Runtime.RenderObject;

namespace Inno.Runtime.Component;

/// <summary>
/// SpriteRenderer component draws a 2D texture with sorting support.
/// </summary>
public class SpriteRenderer : GameBehavior
{
    public static readonly int MAX_LAYER_DEPTH = 1000;
    private float m_opacity = 1f;
    private int m_layerDepth = 0;
    
    public override ComponentTag orderTag => ComponentTag.Render;

    /// <summary>
    /// The sprite to render.
    /// </summary>
    [SerializableProperty]
    public Sprite sprite { get; set; } = Sprite.SolidColor(new Vector2(1, 1));
    
    /// <summary>
    /// The color of the sprite.
    /// </summary>
    [SerializableProperty]
    public Color color { get; set; } = Color.WHITE;
    
    /// <summary>
    /// Opacity to render the sprite. Ranging from 0-1;
    /// </summary>
    [SerializableProperty]
    public float opacity
    {
        get => m_opacity;
        set => m_opacity = MathHelper.Clamp(value, 0f, 1f);
    }
    
    /// <summary>
    /// Layer depth for sorting sprites. It is ranged from 0 to 1000, where 0 is the lowest layer and 1000 is the highest layer.
    /// </summary>
    [SerializableProperty]
    public int layerDepth
    {
        get => m_layerDepth;
        set
        {
            if (m_layerDepth == value) return;
            m_layerDepth = MathHelper.Clamp(value, 0, MAX_LAYER_DEPTH);
        }
    }

}