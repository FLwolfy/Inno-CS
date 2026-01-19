using System;
using Inno.Core.Events;
using Inno.Core.Math;

namespace Inno.Platform.Window;

public enum WindowBackend
{
    Veldrid_Sdl2
}

[Flags]
public enum WindowCreateFlags
{
    None         = 0,

    Hidden       = 1 << 0,
    Resizable    = 1 << 1,
    Decorated   = 1 << 2,   // true = bordered
    AlwaysOnTop = 1 << 3,
    SkipTaskbar = 1 << 4,

    ToolWindow  = 1 << 5,   // utility / inspector / palette
    Popup       = 1 << 6,   // popup / menu / tooltip

    AllowHighDpi= 1 << 7,
    Fullscreen  = 1 << 8,
}


public struct WindowInfo
{
    public string name;
    public int x;
    public int y;
    public int width;
    public int height;
    public WindowCreateFlags flags;
}

public interface IWindow : IDisposable
{
    bool exists { get; }
    
    int width { get; set; }
    int height { get; set; }
    bool resizable { get; set; }
    bool decorated { get; set; }
    string title { get; set; }

    void Show();
    void Hide();
    void Close();

    event Action Resized;
    event Action Closed;
    event Action<Vector2> Moved;
    event Action FocusGained;

    EventSnapshot PumpEvents(EventDispatcher? dispatcher);
}
