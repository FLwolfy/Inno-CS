using System;
using Inno.Platform.Graphics;
using Inno.Platform.Graphics.Bridge;
using Inno.Platform.ImGui;
using Inno.Platform.ImGui.Bridge;
using Inno.Platform.Window;
using Inno.Platform.Window.Bridge;

namespace Inno.Platform;

public static class PlatformAPI
{
    public static IWindow CreateWindow(WindowInfo info, WindowBackend backend)
    {
        if (backend == WindowBackend.Veldrid_Sdl2)
            return new VeldridSdl2Window(info);

        throw new NotSupportedException($"Window backend {backend} is not supported.");
    }

    public static IGraphicsDevice CreateGraphicsDevice(IWindow window, GraphicsBackend backend)
    {
        if (window is VeldridSdl2Window vsw)
            return new VeldridGraphicsDevice(vsw, backend);
        
        throw new NotSupportedException($"GraphicsDevice for window {window.GetType()} is not supported.");
    }
    
    public static void SetupImGuiImpl(IWindow window, IGraphicsDevice graphicsDevice, ImGuiColorSpaceHandling colorSpaceHandling)
    {
        if (window is VeldridSdl2Window vsw && graphicsDevice is VeldridGraphicsDevice vgd)
        {
            IImGui.impl = new ImGuiNETVeldrid(vgd, vsw, colorSpaceHandling);
            return;
        }

        throw new NotSupportedException($"ImGuiRenderer for graphicsDevice {graphicsDevice.GetType()} or window {window.GetType()} is not supported.");
    }
}