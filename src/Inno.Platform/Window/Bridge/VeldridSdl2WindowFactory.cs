using System;
using System.Collections.Generic;

using Inno.Core.Events;
using Inno.Core.Math;
using Inno.Platform.Graphics;
using Inno.Platform.Graphics.Bridge;

using InnoGraphicsBackend = Inno.Platform.Graphics.GraphicsBackend;

using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace Inno.Platform.Window.Bridge;

internal class VeldridSdl2WindowFactory : IWindowFactory
{
    // Grahpics
    private readonly Dictionary<IWindow, Swapchain> m_windowSwapchains;
    public IWindow mainWindow { get; }
    public IGraphicsDevice graphicsDevice { get; }
    
    // Cursor
    private static readonly Dictionary<Input.MouseCursor, SDL_Cursor> CURSOR_MAP = new()
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
    
    // SDL native window delegate
    private unsafe delegate uint SdlGetGlobalMouseStateT(int* x, int* y);
    private readonly SdlGetGlobalMouseStateT? m_pSdlGetGlobalMouseState;
    private unsafe delegate int SdlGetDisplayUsableBoundsT(int displayIndex, Rectangle* rect);
    private static SdlGetDisplayUsableBoundsT? m_pSdlGetDisplayUsableBounds;

    internal VeldridSdl2WindowFactory(in WindowInfo mainWindowInfo, in InnoGraphicsBackend graphicsBackend)
    {
        var mwInner = new VeldridSdl2Window(mainWindowInfo);
        var gdInner = new VeldridGraphicsDevice(mwInner, graphicsBackend);
        mwInner.frameBuffer = gdInner.swapchainFrameBuffer;

        mainWindow = mwInner;
        graphicsDevice = gdInner;
        m_windowSwapchains = new Dictionary<IWindow, Swapchain>
        {
            [mainWindow] = gdInner.inner.MainSwapchain
        };
        
        m_pSdlGetGlobalMouseState ??= Sdl2Native.LoadFunction<SdlGetGlobalMouseStateT>("SDL_GetGlobalMouseState");
        m_pSdlGetDisplayUsableBounds ??= Sdl2Native.LoadFunction<SdlGetDisplayUsableBoundsT>("SDL_GetDisplayUsableBounds");
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
        window.frameBuffer = new VeldridFrameBuffer(((VeldridGraphicsDevice)graphicsDevice).inner, swapchain.Framebuffer);
        
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

    public int GetDisplayNumber()
    {
        return Sdl2Native.SDL_GetNumVideoDisplays();
    }

    public Rect GetDisplayBounds(int displayIndex)
    {
        Rectangle rect;

        unsafe
        {
            Sdl2Native.SDL_GetDisplayBounds(displayIndex, &rect);
        }
        
        return new(rect.X, rect.Y, rect.Width, rect.Height);
    }

    public Rect GetUsableDisplayBounds(int displayIndex)
    {
        Rectangle r = new Rectangle();

        unsafe
        {
            m_pSdlGetDisplayUsableBounds?.Invoke(displayIndex, &r);
        }
        
        return new(r.X, r.Y, r.Width, r.Height);
    }
    
    public void ShowCursor(bool show)
    {
        Sdl2Native.SDL_ShowCursor(show ? 1 : 0);
    }

    public void SetCursor(Input.MouseCursor cursor)
    {
        Sdl2Native.SDL_SetCursor(CURSOR_MAP.TryGetValue(cursor, out var sdlCursor)
            ? sdlCursor
            : CURSOR_MAP[Input.MouseCursor.Arrow]);
    }

    public Vector2Int GetGlobalMousePos()
    {
        int x = 0;
        int y = 0;
        
        unsafe
        {
            m_pSdlGetGlobalMouseState?.Invoke(&x, &y);
        }
        
        return new(x, y);
    }

    public IReadOnlyList<Input.MouseButton> GetGlobalMouseButton()
    {
        List<Input.MouseButton> mouseButtons = [];
        
        unsafe
        {
            int _, __;
            uint? buttons = m_pSdlGetGlobalMouseState?.Invoke(&_, &__);
            
            // SDL: 1=Left, 2=Middle, 4=Right
            var left = (buttons & 0b0001) != 0; // Left
            var right = (buttons & 0b0100) != 0; // Right
            var middle = (buttons & 0b0010) != 0; // Middle
            
            if (left) mouseButtons.Add(Input.MouseButton.Left);
            if (right) mouseButtons.Add(Input.MouseButton.Right);
            if (middle) mouseButtons.Add(Input.MouseButton.Middle);
        }
        
        return mouseButtons;
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