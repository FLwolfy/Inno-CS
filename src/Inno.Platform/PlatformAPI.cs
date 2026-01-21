using System;
using Inno.Platform.Display;
using Inno.Platform.Display.Bridge;
using Inno.Platform.Graphics;
using Inno.Platform.Graphics.Bridge;
using Inno.Platform.Window;
using Inno.Platform.Window.Bridge;

namespace Inno.Platform;

public enum PlatformBackend
{
    Veldrid_Sdl2
}

public sealed class PlatformAPI : IDisposable
{
    public IWindowSystem windowSystem { get; private set; }
    public IDisplaySystem displaySystem { get; private set; }
    public IGraphicsDevice graphicsDevice { get; private set; }

    public PlatformAPI(
        WindowInfo mainWindowInfo,
        PlatformBackend platformBackend,
        GraphicsBackend graphicsBackend)
    {
        switch (platformBackend)
        {
            case PlatformBackend.Veldrid_Sdl2:
            {
                windowSystem = new VeldridSdl2WindowSystem(mainWindowInfo, graphicsBackend, out var vgd) ;
                displaySystem = new VeldridSdl2DisplaySystem();
                graphicsDevice = vgd;
                
                return;
            }
        }
        
        throw new PlatformNotSupportedException($"Platform backend {platformBackend} is not supported.");
    }

    public void Dispose()
    {
        windowSystem.Dispose();
        displaySystem.Dispose();
        graphicsDevice.Dispose();
    }
}