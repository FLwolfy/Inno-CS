using System.Collections.Generic;
using Inno.Core.Events;
using Inno.Platform.Graphics;
using Inno.Platform.Graphics.Bridge;
using InnoGraphicsBackend = Inno.Platform.Graphics.GraphicsBackend;

using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace Inno.Platform.Window.Bridge;

internal class VeldridSdl2WindowFactory : IWindowFactory
{
    public IWindow mainWindow { get; }
    public IGraphicsDevice graphicsDevice { get; }
    
    private readonly Dictionary<IWindow, Swapchain> m_windowSwapchains;
    private readonly Dictionary<Input.MouseCursor, SDL_Cursor> m_cursorMap;

    internal VeldridSdl2WindowFactory(in WindowInfo mainWindowInfo, in InnoGraphicsBackend graphicsBackend)
    {
        var mwInner = new VeldridSdl2Window(mainWindowInfo);
        var gdInner = new VeldridGraphicsDevice(mwInner, graphicsBackend);
        
        mainWindow = mwInner;
        graphicsDevice = gdInner;
        m_windowSwapchains = new Dictionary<IWindow, Swapchain>
        {
            [mainWindow] = gdInner.inner.MainSwapchain
        };
        m_cursorMap = new Dictionary<Input.MouseCursor, SDL_Cursor>
        {
            [Input.MouseCursor.Arrow] = 
                Sdl2Native.SDL_CreateSystemCursor(SDL_SystemCursor.Arrow),
            [Input.MouseCursor.TextInput] =
                Sdl2Native.SDL_CreateSystemCursor(SDL_SystemCursor.IBeam),
            [Input.MouseCursor.ResizeAll] =
                Sdl2Native.SDL_CreateSystemCursor(SDL_SystemCursor.SizeAll),
            [Input.MouseCursor.ResizeNS] =
                Sdl2Native.SDL_CreateSystemCursor(SDL_SystemCursor.SizeNS),
            [Input.MouseCursor.ResizeEW] =
                Sdl2Native.SDL_CreateSystemCursor(SDL_SystemCursor.SizeWE),
            [Input.MouseCursor.ResizeNESW] =
                Sdl2Native.SDL_CreateSystemCursor(SDL_SystemCursor.SizeNESW),
            [Input.MouseCursor.ResizeNWSE] =
                Sdl2Native.SDL_CreateSystemCursor(SDL_SystemCursor.SizeNWSE),
            [Input.MouseCursor.Hand] =
                Sdl2Native.SDL_CreateSystemCursor(SDL_SystemCursor.Hand),
        };
    }
    
    public IWindow CreateWindow(in WindowInfo info)
    {
        var window = new VeldridSdl2Window(info);
        var vgd = (graphicsDevice as VeldridGraphicsDevice)!.inner;
        var size = VeldridSdl2HiDpi.GetFramebufferSize(window.inner);
        
        SwapchainSource scSource = VeldridStartup.GetSwapchainSource(window.inner);
        SwapchainDescription scDesc = new SwapchainDescription(
            scSource, 
            (uint)size.x, 
            (uint)size.y, 
            vgd.SwapchainFramebuffer.OutputDescription.DepthAttachment?.Format,
            true, 
            false
        );
        
        var swapchain = vgd.ResourceFactory.CreateSwapchain(scDesc);
        swapchain.Resize((uint)size.x, (uint)size.y);
        window.Resized += () =>
        {
            var s = VeldridSdl2HiDpi.GetFramebufferSize(window.inner);
            swapchain.Resize((uint)s.x, (uint)s.y);
        };
        m_windowSwapchains[window] = swapchain;
        
        return window;
    }

    public void DestroyWindow(IWindow window)
    {
        var swapchain = m_windowSwapchains[window];
        swapchain.Dispose();
        m_windowSwapchains.Remove(window);
        window.Dispose();
    }
    
    public void SwapWindowBuffers(IWindow window)
    {
        (graphicsDevice as VeldridGraphicsDevice)!.inner.SwapBuffers(m_windowSwapchains[window]);
    }
    
    public void ShowCursor(bool show)
    {
        Sdl2Native.SDL_ShowCursor(show ? 1 : 0);
    }

    public void SetCursor(Input.MouseCursor cursor)
    {
        Sdl2Native.SDL_SetCursor(m_cursorMap.TryGetValue(cursor, out var sdlCursor)
            ? sdlCursor
            : m_cursorMap[Input.MouseCursor.Arrow]);
    }
    
    public void Dispose()
    {
        mainWindow.Dispose();
        graphicsDevice.Dispose();

        foreach (var windowSwapchain in m_windowSwapchains.Values)
        {
            windowSwapchain.Dispose();
        }
        m_windowSwapchains.Clear();
    }

}