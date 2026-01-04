using System;
using System.Collections.Generic;
using Inno.Platform.Graphics;

namespace Inno.Graphics.Targets;

public static class RenderTargetPool
{
    private static readonly Dictionary<string, RenderTarget> TARGETS = new();
    private static IGraphicsDevice m_device = null!;

    public static void Initialize(IGraphicsDevice device)
    {
        m_device = device;
        TARGETS["main"] = new RenderTarget(new RenderContext(device, device.swapchainFrameBuffer));
    }

    public static RenderTarget? Get(string name) => TARGETS.GetValueOrDefault(name);
    public static RenderTarget GetMain() => TARGETS["main"];

    public static RenderTarget Create(string name, FrameBufferDescription desc)
    {
        if (TARGETS.ContainsKey(name)) throw new InvalidOperationException($"Already exists a framebuffer named {name}!");
        var result = new RenderTarget(m_device, desc);
        TARGETS[name] = result;
        return result;
    }

    public static void Release(string name)
    {
        if (TARGETS.TryGetValue(name, out var rt))
        {
            rt.Dispose();
            TARGETS.Remove(name);
        }
    }

    public static void Clear()
    {
        foreach (var rt in TARGETS.Values) rt.Dispose();
        TARGETS.Clear();
    }
}