using System;
using System.Runtime.InteropServices;

using Inno.Platform.Window;

using ImGuiNET;
using Inno.Core.Events;

namespace Inno.Platform.ImGui.Bridge;

internal class ImGuiWindow : IDisposable
{
    private GCHandle m_gcHandle;
    
    private readonly IWindowFactory m_windowFactory;
    private readonly IWindow m_window;
    private readonly ImGuiViewportPtr m_viewportPtr;
    private readonly bool m_isMainWindow;
    
    public IWindow window => m_window;
    public ImGuiViewportPtr viewportPtr => m_viewportPtr;

    public static ImGuiWindow? currentWindow;

    public ImGuiWindow(IWindowFactory windowFactory, ImGuiViewportPtr vp)
    {
        m_gcHandle = GCHandle.Alloc(this);
        m_windowFactory = windowFactory;
        m_viewportPtr = vp;
        m_isMainWindow = false;

        var flags = WindowCreateFlags.Hidden | WindowCreateFlags.AllowHighDpi;
        if ((vp.Flags & ImGuiViewportFlags.NoTaskBarIcon) != 0)
        {
            flags |= WindowCreateFlags.SkipTaskbar;
        }
        if ((vp.Flags & ImGuiViewportFlags.NoDecoration) == 0)
        {
            flags |= WindowCreateFlags.Decorated;
        }
        else
        {
            flags |= WindowCreateFlags.Resizable;
        }
        
        if ((vp.Flags & ImGuiViewportFlags.TopMost) != 0)
        {
            flags |= WindowCreateFlags.AlwaysOnTop;
        }

        var info = new WindowInfo
        {
            name = "ImGui",
            x = (int)vp.Pos.X, 
            y = (int)vp.Pos.Y,
            width = (int)vp.Size.X,
            height = (int)vp.Size.Y,
            flags = flags,
        };

        m_window = windowFactory.CreateWindow(info);
        
        m_window.Resized += () => m_viewportPtr.PlatformRequestResize = true;
        m_window.Closed += () => m_viewportPtr.PlatformRequestClose = true;
        m_window.Moved += _ => m_viewportPtr.PlatformRequestMove = true;
        m_window.FocusGained += () => currentWindow = this;
        
        vp.PlatformUserData = (IntPtr)m_gcHandle;
    }

    public ImGuiWindow(IWindowFactory windowFactory, ImGuiViewportPtr vp, IWindow mainWindow)
    {
        m_gcHandle = GCHandle.Alloc(this);
        m_viewportPtr = vp;
        m_windowFactory = windowFactory;
        m_window = mainWindow;
        m_isMainWindow = true;
        vp.PlatformUserData = (IntPtr)m_gcHandle;
    }

    // public EventSnapshot PumpEvents()
    // {
    //     return m_window.PumpEvents(null);
    // }

    public void Dispose()
    {
        if (currentWindow == this) currentWindow = null;

        m_gcHandle.Free();
        
        if (!m_isMainWindow)
        {
            m_windowFactory.DestroyWindow(m_window);
        }
    }
}