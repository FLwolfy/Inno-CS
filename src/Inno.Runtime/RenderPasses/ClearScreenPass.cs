using Inno.Core.Math;
using Inno.Graphics;
using Inno.Graphics.Pass;

namespace Inno.Runtime.RenderPasses;

/// <summary>
/// Clears the screen before rendering.
/// </summary>
public class ClearScreenPass(Color? clearColor = null) : RenderPass
{
    private static readonly Color DEFAULT_CLEAR_COLOR = Color.CORNFLOWERBLUE;
    private readonly Color m_clearColor = clearColor ?? DEFAULT_CLEAR_COLOR;
    
    public override RenderPassTag orderTag => RenderPassTag.ClearScreen;

    public override void OnRender(RenderContext ctx)
    {
        Renderer2D.ClearColor(ctx, m_clearColor);
    }
}