using System;

using Inno.ImGui.Backend;
using Inno.Platform.Display;
using Inno.Platform.Graphics;
using Inno.Platform.Window;

namespace Inno.ImGui;

public enum ImGuiBackend
{
    ImGui_DotNET
}

/// <summary>
/// Identifies the kind of color space handling that ImGui uses.
/// </summary>
public enum ImGuiColorSpaceHandling
{
    /// <summary>
    /// Legacy-style color space handling. In this mode, the renderer will not convert sRGB vertex colors into linear space
    /// before blending them.
    /// </summary>
    Legacy = 0,
    /// <summary>
    /// Improved color space handling. In this mode, the render will convert sRGB vertex colors into linear space before
    /// blending them with colors from user Textures.
    /// </summary>
    Linear = 1,
}

/// <summary>
/// Static API for ImGui.
/// Responsible for handling frame lifecycle, rendering ImGui draw data,
/// and binding textures for use in ImGui.
/// </summary>
public static class ImGuiHost
{
    internal const ImGuiFontSize C_DEFAULT_FONT_SIZE = ImGuiFontSize.Medium;
    private static ImGuiNETBackend impl { get; set; } = null!;
    
    /// <summary>
    /// Create the imGui backend with given windowFactory and specified imguiBackend
    /// </summary>
    public static void Initialize(
        IWindowSystem windowSystem, 
        IDisplaySystem displaySystem, 
        IGraphicsDevice graphicsDevice,
        ImGuiBackend imGuiBackend, 
        ImGuiColorSpaceHandling colorSpaceHandling)
    {
        switch (imGuiBackend)
        {
            case ImGuiBackend.ImGui_DotNET:
            {
                impl = new ImGuiNETBackend(windowSystem, displaySystem, graphicsDevice, colorSpaceHandling);
                return;
            }
            
            default: throw new NotSupportedException($"ImGui backend {imGuiBackend} is not supported.");
        }

    }
    
    /// <summary>
    /// Starts a new ImGui frame. Should be called before any ImGui calls each frame.
    /// </summary>
    public static void BeginLayout(float deltaTime) => impl.BeginLayoutImpl(deltaTime);

    /// <summary>
    /// Ends the ImGui frame and finalizes draw data.
    /// </summary>
    public static void EndLayout() => impl.EndLayoutImpl();
    
    /// <summary>
    /// Gets or Binds a texture for use by ImGui and returns a texture ID handle.
    /// </summary>
    /// <param name="texture">The texture to bind.</param>
    /// <returns>An IntPtr handle used by ImGui to reference this texture.</returns>
    public static IntPtr GetOrBindTexture(ITexture texture) => impl.GetOrBindTextureImpl(texture);
    
    /// <summary>
    /// Unbinds a previously bound texture from ImGui.
    /// </summary>
    /// <param name="texture">The texture to unbind.</param>
    public static void UnbindTexture(ITexture texture) => impl.UnbindTextureImpl(texture);
    
    /// <summary>
    /// Push a specific font style.
    /// </summary>
    public static void UseFont(ImGuiAlias alias) => impl.UseFontImpl(alias.style, alias.size);
    
    /// <summary>
    /// Push a specific font style.
    /// </summary>
    public static void UseFont(ImGuiFontStyle style, ImGuiFontSize size) => impl.UseFontImpl(style, (float)size);
    
    /// <summary>
    /// Push a specific font style.
    /// </summary>
    public static void UseFont(ImGuiFontStyle style, float? size = null) => impl.UseFontImpl(style, size);
    
    /// <summary>
    /// Get the font style and size in the current context.
    /// </summary>
    /// <returns></returns>
    public static ImGuiAlias GetCurrentFont() => impl.GetCurrentFontImpl();
    
    /// <summary>
    /// Zoom in or out based on the given zoom rate.
    /// </summary>
    public static void Zoom(float zoomRate) => impl.ZoomImpl(zoomRate);
    
    /// <summary>
    /// Gets the pointer to the main ImGui context.
    /// </summary>
    public static IntPtr mainMainContextPtr => impl.mainMainContextPtrImpl;
    
    /// <summary>
    /// Gets the pointer to the virtual ImGui context.
    /// </summary>
    public static IntPtr virtualContextPtr => impl.virtualContextPtrImpl;
    
    /// <summary>
    /// Sets the UI storage data into ImGui.ini.
    /// </summary>
    public static void SetStorageData(string key, object? value) => impl.SetStorageDataImpl(key, value);
    
    /// <summary>
    /// Gets the UI storage data from ImGui.ini.
    /// </summary>
    public static T? GetStorageData<T>(string key, T? defaultValue = default) => impl.GetStorageDataImpl(key, defaultValue);

    /// <summary>
    /// Dispose the implementation of ImGui.
    /// </summary>
    public static void DisposeImpl()
    {
        impl.Dispose();
    }
}