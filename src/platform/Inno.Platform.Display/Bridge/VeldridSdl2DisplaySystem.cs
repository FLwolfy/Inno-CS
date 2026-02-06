using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Core.Input;
using Inno.Core.Mathematics;
using Inno.Platform.Graphics;
using Inno.Platform.Graphics.Bridge;

using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace Inno.Platform.Display.Bridge;

internal class VeldridSdl2DisplaySystem : IDisplaySystem
{
    // Cursor
    private static readonly Dictionary<MouseCursor, SDL_Cursor> CURSOR_MAP = new()
    {
        [MouseCursor.Arrow] = 
            Sdl2Native.SDL_CreateSystemCursor(SDL_SystemCursor.Arrow),
        [MouseCursor.TextInput] =
            Sdl2Native.SDL_CreateSystemCursor(SDL_SystemCursor.IBeam),
        [MouseCursor.ResizeAll] =
            Sdl2Native.SDL_CreateSystemCursor(SDL_SystemCursor.SizeAll),
        [MouseCursor.ResizeNS] =
            Sdl2Native.SDL_CreateSystemCursor(SDL_SystemCursor.SizeNS),
        [MouseCursor.ResizeEW] =
            Sdl2Native.SDL_CreateSystemCursor(SDL_SystemCursor.SizeWE),
        [MouseCursor.ResizeNESW] =
            Sdl2Native.SDL_CreateSystemCursor(SDL_SystemCursor.SizeNESW),
        [MouseCursor.ResizeNWSE] =
            Sdl2Native.SDL_CreateSystemCursor(SDL_SystemCursor.SizeNWSE),
        [MouseCursor.Hand] =
            Sdl2Native.SDL_CreateSystemCursor(SDL_SystemCursor.Hand),
    };
    
    // SDL native window delegate
    private unsafe delegate uint SdlGetGlobalMouseStateT(int* x, int* y);
    private SdlGetGlobalMouseStateT? m_pSdlGetGlobalMouseState;
    private unsafe delegate int SdlGetDisplayUsableBoundsT(int displayIndex, Veldrid.Rectangle* rect);
    private SdlGetDisplayUsableBoundsT? m_pSdlGetDisplayUsableBounds;
    
    // Window Graphics
    private readonly Dictionary<IWindow, Veldrid.Swapchain> m_windowSwapchains;
    private readonly IGraphicsDevice m_graphicsDevice; // This should be disposed outside.

    // Window Properties
    public IWindow mainWindow { get; }
    public IEnumerable<IWindow> extraWindows => m_windowSwapchains.Keys.Where(w => w != mainWindow);

    internal VeldridSdl2DisplaySystem(
        in WindowInfo mainWindowInfo, 
        in GraphicsBackend graphicsBackend,
        out VeldridGraphicsDevice graphicsDevice)
    {
        // Input native delegate
        m_pSdlGetGlobalMouseState ??= Sdl2Native.LoadFunction<SdlGetGlobalMouseStateT>("SDL_GetGlobalMouseState");
        m_pSdlGetDisplayUsableBounds ??= Sdl2Native.LoadFunction<SdlGetDisplayUsableBoundsT>("SDL_GetDisplayUsableBounds");
        
        // Window and 
        var mwInner = new VeldridSdl2Window(mainWindowInfo);
        mainWindow = mwInner;
        
        // Graphics
        var deviceOptions = new Veldrid.GraphicsDeviceOptions(
            debug: true,
            null,
            syncToVerticalBlank: false,
            resourceBindingModel: Veldrid.ResourceBindingModel.Improved,
            preferDepthRangeZeroToOne: true,
            preferStandardClipSpaceYDirection: true
        );
        var innerDevice = VeldridStartup.CreateGraphicsDevice(mwInner.inner, deviceOptions, ToVeldridGraphicsBackend(graphicsBackend));
        graphicsDevice = new VeldridGraphicsDevice(innerDevice, graphicsBackend);
        mwInner.frameBuffer = graphicsDevice.swapchainFrameBuffer;
        m_graphicsDevice = graphicsDevice;
        
        // Swapchain Register
        m_windowSwapchains = new Dictionary<IWindow, Veldrid.Swapchain>
        {
            [mainWindow] = graphicsDevice.inner.MainSwapchain
        };
        
        // Resize Events: Ensure swapchain/backbuffer matches drawable pixel size on HiDPI displays.
        {
            var size = VeldridSdl2HiDpi.GetFramebufferSize(mwInner.inner);
            if (size != mwInner.size)
            {
                graphicsDevice.swapchainFrameBuffer.Resize(size.x, size.y);
            }
        }
        
        mwInner.inner.Resized += () =>
        {
            var size = VeldridSdl2HiDpi.GetFramebufferSize(mwInner.inner);
            m_graphicsDevice.swapchainFrameBuffer.Resize(size.x, size.y);
        };
    }

    private Veldrid.GraphicsBackend ToVeldridGraphicsBackend(GraphicsBackend graphicsBackend)
    {
        return graphicsBackend switch
        {
            GraphicsBackend.Vulkan => Veldrid.GraphicsBackend.Vulkan,
            GraphicsBackend.Direct3D11 => Veldrid.GraphicsBackend.Direct3D11,
            GraphicsBackend.OpenGL => Veldrid.GraphicsBackend.OpenGL,
            GraphicsBackend.Metal => Veldrid.GraphicsBackend.Metal,
            GraphicsBackend.OpenGLES => Veldrid.GraphicsBackend.OpenGLES,
            
            _ => throw new NotSupportedException($"Graphics backend {graphicsBackend} not supported")
        };
    }

    #region Display
    
    public int GetDisplayNumber()
    {
        return Sdl2Native.SDL_GetNumVideoDisplays();
    }

    public Rect GetDisplayBounds(int displayIndex)
    {
        Veldrid.Rectangle rect;

        unsafe
        {
            Sdl2Native.SDL_GetDisplayBounds(displayIndex, &rect);
        }
        
        return new Rect(rect.X, rect.Y, rect.Width, rect.Height);
    }

    public Rect GetUsableDisplayBounds(int displayIndex)
    {
        Veldrid.Rectangle r = new Veldrid.Rectangle();

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

    public void SetCursor(MouseCursor cursor)
    {
        Sdl2Native.SDL_SetCursor(CURSOR_MAP.TryGetValue(cursor, out var sdlCursor)
            ? sdlCursor
            : CURSOR_MAP[MouseCursor.Arrow]);
    }
    
    #endregion
    
    #region Input

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

    public IReadOnlyList<MouseButton> GetGlobalMouseButton()
    {
        List<MouseButton> mouseButtons = [];
        
        unsafe
        {
            int _, __;
            uint? buttons = m_pSdlGetGlobalMouseState?.Invoke(&_, &__);
            
            // SDL: 1=Left, 2=Middle, 4=Right
            var left = (buttons & 0b0001) != 0; // Left
            var right = (buttons & 0b0100) != 0; // Right
            var middle = (buttons & 0b0010) != 0; // Middle
            
            if (left) mouseButtons.Add(MouseButton.Left);
            if (right) mouseButtons.Add(MouseButton.Right);
            if (middle) mouseButtons.Add(MouseButton.Middle);
        }
        
        return mouseButtons;
    }
    
    #endregion

    #region Window

    public IWindow CreateWindow(in WindowInfo info)
    {
        var window = new VeldridSdl2Window(info);
        var vgd = (m_graphicsDevice as VeldridGraphicsDevice)!.inner;
        var size = VeldridSdl2HiDpi.GetFramebufferSize(window.inner);
        
        Veldrid.SwapchainSource scSource = VeldridStartup.GetSwapchainSource(window.inner);
        Veldrid.SwapchainDescription scDesc = new Veldrid.SwapchainDescription(
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
        window.frameBuffer = new VeldridFrameBuffer(((VeldridGraphicsDevice)m_graphicsDevice).inner, swapchain.Framebuffer);
        
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
        if (window == mainWindow)
        {
            (m_graphicsDevice as VeldridGraphicsDevice)!.inner.SwapBuffers();
        }
        else
        {
            SwapExtraWindowBuffers(window);
        }
    }

    private void SwapExtraWindowBuffers(IWindow window)
    {
        var vgd = (m_graphicsDevice as VeldridGraphicsDevice)!.inner;
        
        // Check occlusion state
        var focusWindows = m_windowSwapchains.Keys.Where(w => w.focused).ToArray();
        if (!focusWindows.Contains(window))
        {
            foreach (var focus in focusWindows)
            {
                if (focus.bounds.Contains(window.bounds)) return;
            }
        }
        
        vgd.SwapBuffers(m_windowSwapchains[window]);
    }
    
    #endregion

    public void Dispose()
    {
        // Input
        m_pSdlGetGlobalMouseState = null;
        m_pSdlGetDisplayUsableBounds = null;
        
        // Window
        mainWindow.Dispose();

        foreach (var windowSwapchain in m_windowSwapchains.Values)
        {
            windowSwapchain.Dispose();
        }
        m_windowSwapchains.Clear();
    }
}