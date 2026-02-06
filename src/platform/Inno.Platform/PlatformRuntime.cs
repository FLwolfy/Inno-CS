using System;
using Inno.Platform.Display;
using Inno.Platform.Display.Bridge;
using Inno.Platform.Graphics;

namespace Inno.Platform;

/// <summary>
/// Owns and manages all platform subsystems for a running application instance.
/// </summary>
public sealed class PlatformRuntime : IDisposable
{
    public readonly IDisplaySystem displaySystem;
    public readonly IGraphicsDevice graphicsDevice;

    internal PlatformRuntime(
        WindowInfo mainWindowInfo,
        PlatformBackend platformBackend,
        GraphicsBackend graphicsBackend)
    {
        switch (platformBackend)
        {
            case PlatformBackend.Veldrid_Sdl2:
            {
                displaySystem = new VeldridSdl2DisplaySystem(
                    mainWindowInfo,
                    graphicsBackend,
                    out var vgd);

                graphicsDevice = vgd;
                return;
            }
        }

        throw new PlatformNotSupportedException($"Platform backend {platformBackend} is not supported.");
    }

    public void Dispose()
    {
        displaySystem.Dispose();
        graphicsDevice.Dispose();
    }
}