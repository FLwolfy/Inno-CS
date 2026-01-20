using System;

using Inno.Platform.Graphics;
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
}