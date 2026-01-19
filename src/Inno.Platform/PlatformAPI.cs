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
    public static IWindowFactory CreateWindowFactory(
        WindowInfo mainWindowInfo, 
        WindowBackend windowBackend,
        GraphicsBackend graphicsBackend)
    {
        if (windowBackend == WindowBackend.Veldrid_Sdl2)
            return new VeldridSdl2WindowFactory(mainWindowInfo, graphicsBackend);

        throw new NotSupportedException($"Window backend {windowBackend} is not supported.");
    }
    
    public static void SetupImGuiImpl(IWindowFactory windowFactory, ImGuiColorSpaceHandling colorSpaceHandling)
    {
        if (windowFactory is VeldridSdl2WindowFactory vwf)
        {
            IImGui.impl = new ImGuiImpl(vwf, colorSpaceHandling);
            return;
        }

        throw new NotSupportedException($"ImGuiRenderer for window factory {windowFactory.GetType()} is not supported.");
    }
}