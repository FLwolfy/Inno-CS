using System;

using Inno.Core.Events;
using Inno.Core.Math;

using Veldrid;
using Veldrid.Sdl2;

namespace Inno.Platform.Window.Bridge;

internal class VeldridSdl2Window : IWindow
{
    internal Sdl2Window inner { get; }
    internal InputSnapshot inputSnapshot { get; private set; }
    
    private bool m_isWindowSizeDirty = false;
    private readonly EventSnapshot m_eventSnapshot = new EventSnapshot();

    public bool exists => inner.Exists;
    public int x
    {
        get => inner.X;
        set => inner.X = value;
    }
    public int y
    {
        get => inner.Y;
        set => inner.Y = value;
    }
    public int width
    {
        get => inner.Width;
        set => inner.Width = value;
    }
    public int height
    {
        get => inner.Height;
        set => inner.Height = value;
    }
    public bool resizable
    {
        get => inner.Resizable;
        set => inner.Resizable = value;
    }
    public bool decorated
    {
        get => inner.BorderVisible;
        set => inner.BorderVisible = value;
    }
    public string title
    {
        get => inner.Title;
        set => inner.Title = value;
    }

    public VeldridSdl2Window(WindowInfo info)
    {
        var flags = MapToSdlFlags(info.flags);
        flags |= SDL_WindowFlags.AllowHighDpi; // TODO: Move this outside
        flags |= SDL_WindowFlags.Resizable;

        inner = new Sdl2Window(
            info.name,
            100, 100,
            info.width,
            info.height,
            flags,
            false);

        Resized += () => m_isWindowSizeDirty = true;
        inner.Resized += Resized;
        inner.Moved += p => Moved?.Invoke(new Vector2(p.X, p.Y));
        inner.Closed += Closed;
        inner.FocusGained += FocusGained;
        
        inputSnapshot = inner.PumpEvents();
    }

    public void Show() => inner.Visible = true;
    public void Hide() => inner.Visible = false;
    public void Close() => inner.Close();
    
    // Actions
    public event Action? Resized;
    public event Action? Closed;
    public event Action<Vector2>? Moved;
    public event Action? FocusGained;

    public void PumpEvents(EventDispatcher dispatcher)
    {
        // Input Events
        inputSnapshot = inner.PumpEvents();
        m_eventSnapshot.Clear();
        VeldridSdl2InputAdapter.AdaptInputEvents(inputSnapshot, e =>
        {
            m_eventSnapshot.AddEvent(e);
            dispatcher.PushEvent(e);
        });
        foreach (var c in inputSnapshot.KeyCharPresses)
        {
            m_eventSnapshot.AddInputChar(c);
        }
        
        // Application Events
        if (m_isWindowSizeDirty)
        {
            var resizeEvent = new WindowResizeEvent(width, height);
            m_eventSnapshot.AddEvent(resizeEvent);
            dispatcher.PushEvent(resizeEvent);
            
            m_isWindowSizeDirty = false;
        }
        if (!exists)
        {
            m_eventSnapshot.AddEvent(new WindowCloseEvent());
            dispatcher.PushEvent(new WindowCloseEvent());
        }
    }
    
    public EventSnapshot GetPumpedEvents() => m_eventSnapshot;

    public Vector2Int GetFrameBufferSize() => VeldridSdl2HiDpi.GetFramebufferSize(inner);
    public Vector2 GetFrameBufferScale() => VeldridSdl2HiDpi.GetFramebufferScale(inner);

    public void Dispose()
    {
        inner.Close();
    }
      
    private static SDL_WindowFlags MapToSdlFlags(WindowCreateFlags flags)
    {
        SDL_WindowFlags sdl = 0;

        // Visibility
        if (flags.HasFlag(WindowCreateFlags.Hidden))
            sdl |= SDL_WindowFlags.Hidden;
        else
            sdl |= SDL_WindowFlags.Shown;

        // Decorated / Resize
        if (flags.HasFlag(WindowCreateFlags.Resizable))
            sdl |= SDL_WindowFlags.Resizable;

        if (!flags.HasFlag(WindowCreateFlags.Decorated))
            sdl |= SDL_WindowFlags.Borderless;

        // DPI
        if (flags.HasFlag(WindowCreateFlags.AllowHighDpi))
            sdl |= SDL_WindowFlags.AllowHighDpi;

        // Z-order / taskbar
        if (flags.HasFlag(WindowCreateFlags.AlwaysOnTop))
            sdl |= SDL_WindowFlags.AlwaysOnTop;

        if (flags.HasFlag(WindowCreateFlags.SkipTaskbar))
            sdl |= SDL_WindowFlags.SkipTaskbar;

        // Window type semantics
        if (flags.HasFlag(WindowCreateFlags.ToolWindow))
            sdl |= SDL_WindowFlags.Utility;

        if (flags.HasFlag(WindowCreateFlags.Popup))
            sdl |= SDL_WindowFlags.PopupMenu;

        // Fullscreen
        if (flags.HasFlag(WindowCreateFlags.Fullscreen))
            sdl |= SDL_WindowFlags.FullScreenDesktop;

        return sdl;
    }
}