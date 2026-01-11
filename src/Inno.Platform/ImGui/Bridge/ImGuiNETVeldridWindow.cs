using System;
using System.Runtime.InteropServices;
using ImGuiNET;
using Inno.Platform.Window.Bridge;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace Inno.Platform.ImGui.Bridge;

internal class ImGuiNETVeldridWindow : IDisposable
{
    private GCHandle m_gcHandle;
    
    private readonly GraphicsDevice m_graphicsDevice;
    private readonly ImGuiViewportPtr m_viewportPtr;
    private readonly Sdl2Window m_window;
    private readonly Swapchain m_swapchain;
    private readonly bool m_isMainWindow;
    
    public Sdl2Window window => m_window;
    public Swapchain swapchain => m_swapchain;
    public ImGuiViewportPtr viewportPtr => m_viewportPtr;

    public static ImGuiNETVeldridWindow? currentWindow;

    public ImGuiNETVeldridWindow(GraphicsDevice gd, ImGuiViewportPtr vp)
    {
        m_gcHandle = GCHandle.Alloc(this);
        m_graphicsDevice = gd;
        m_viewportPtr = vp;
        m_isMainWindow = false;

        SDL_WindowFlags flags = SDL_WindowFlags.Hidden | SDL_WindowFlags.AllowHighDpi;
        if ((vp.Flags & ImGuiViewportFlags.NoTaskBarIcon) != 0)
        {
            flags |= SDL_WindowFlags.SkipTaskbar;
        }
        if ((vp.Flags & ImGuiViewportFlags.NoDecoration) != 0)
        {
            flags |= SDL_WindowFlags.Borderless;
        }
        else
        {
            flags |= SDL_WindowFlags.Resizable;
        }

        if ((vp.Flags & ImGuiViewportFlags.TopMost) != 0)
        {
            flags |= SDL_WindowFlags.AlwaysOnTop;
        }

        m_window = new Sdl2Window(
            "ImGui ViewPort Window",
            (int)vp.Pos.X, (int)vp.Pos.Y,
            (int)vp.Size.X, (int)vp.Size.Y,
            flags,
            false);
        m_window.Resized += () => m_viewportPtr.PlatformRequestResize = true;
        m_window.Closed += () => m_viewportPtr.PlatformRequestClose = true;
        m_window.Moved += _ => m_viewportPtr.PlatformRequestMove = true;
        m_window.FocusGained += () => currentWindow = this;
        
        var (fbW, fbH) = VeldridSdl2HiDpi.GetFramebufferSize(m_window);
        SwapchainSource scSource = VeldridStartup.GetSwapchainSource(m_window);
        SwapchainDescription scDesc = new SwapchainDescription(
            scSource, 
            (uint)fbW, 
            (uint)fbH, 
            m_graphicsDevice.SwapchainFramebuffer.OutputDescription.DepthAttachment?.Format,
            true, 
            false
        );

        m_swapchain = m_graphicsDevice.ResourceFactory.CreateSwapchain(scDesc);
        m_swapchain.Resize((uint)fbW, (uint)fbH);
        m_window.Resized += () =>
        {
            var (nw, nh) = VeldridSdl2HiDpi.GetFramebufferSize(m_window);
            m_swapchain.Resize((uint)nw, (uint)nh);
        };

        vp.PlatformUserData = (IntPtr)m_gcHandle;
    }

    public ImGuiNETVeldridWindow(GraphicsDevice gd, ImGuiViewportPtr vp, Sdl2Window mainWindow)
    {
        m_gcHandle = GCHandle.Alloc(this);
        m_graphicsDevice = gd;
        m_viewportPtr = vp;
        m_window = mainWindow;
        m_swapchain = gd.MainSwapchain;
        m_isMainWindow = true;
        vp.PlatformUserData = (IntPtr)m_gcHandle;
    }

    public InputSnapshot PumpEvents()
    {
        return m_window.PumpEvents();
    }

    public void Dispose()
    {
        if (currentWindow == this) currentWindow = null;

        m_gcHandle.Free();
        
        if (!m_isMainWindow)
        {
            m_graphicsDevice.WaitForIdle(); // TODO: Shouldn't be necessary, but Vulkan backend trips a validation error (swapchain in use when disposed).
            m_swapchain.Dispose();
            m_window.Close();
        }
    }
}