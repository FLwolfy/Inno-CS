using System;
using Inno.Graphics.Renderer;
using Inno.Graphics.Resources.GpuResources.Cache;
using Inno.Graphics.Targets;
using Inno.Platform.Graphics;

namespace Inno.Graphics;

public static class RenderGraphics
{
    public static GpuCache gpuCache { get; private set; } = null!;
    public static RenderTargetPool targetPool { get; private set; } = null!;

    public static void Initialize(IGraphicsDevice device)
    {
        gpuCache = new GpuCache();
        targetPool = new RenderTargetPool(device);
        
        Renderer2D.Initialize(device);
    }

    public static void Clear()
    {
        gpuCache.Clear();
        targetPool.Clear();
        
        Renderer2D.CleanResources();
    }
}