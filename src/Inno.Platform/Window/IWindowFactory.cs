using System;
using Inno.Platform.Graphics;

namespace Inno.Platform.Window;

public interface IWindowFactory : IDisposable
{
    IWindow mainWindow { get; }
    IGraphicsDevice graphicsDevice { get; }

    IWindow CreateWindow(in WindowInfo info);
    void DestroyWindow(IWindow window);
    void SwapWindowBuffers(IWindow window);
}
