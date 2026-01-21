using System;
using System.Collections.Generic;
using Inno.Core.Events;
using Inno.Core.Math;
using Inno.Platform.Graphics;

namespace Inno.Platform.Window;

public interface IWindowFactory : IDisposable
{
    IWindow mainWindow { get; }
    IGraphicsDevice graphicsDevice { get; }

    IWindow CreateWindow(in WindowInfo info);
    void DestroyWindow(IWindow window);
    void SwapWindowBuffers(IWindow window);
    
    int GetDisplayNumber();
    Rect GetDisplayBounds(int displayIndex);
    Rect GetUsableDisplayBounds(int displayIndex);
    
    Vector2Int GetGlobalMousePos();
    IReadOnlyList<Input.MouseButton> GetGlobalMouseButton();
    
    void ShowCursor(bool show);
    void SetCursor(Input.MouseCursor cursor);
}
