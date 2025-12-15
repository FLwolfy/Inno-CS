using Inno.Platform.Graphics;
using Inno.Platform.Graphics.Bridge;
using Inno.Platform.ImGui.Bridge;
using Inno.Platform.Window;
using Inno.Platform.Window.Bridge;

namespace Inno.Platform.ImGui;

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
/// Identifies the style of font used.
/// </summary>
public enum ImGuiFontStyle
{
    Regular,
    Bold,
    Italic,
    BoldItalic
}

/// <summary>
/// Interface for ImGui renderer abstraction.
/// Responsible for handling frame lifecycle, rendering ImGui draw data,
/// and binding textures for use in ImGui.
/// </summary>
public interface IImGui : IDisposable
{
    internal static IImGui impl { get; set; } = new ImGuiNoOp();
    
    /// <summary>
    /// Starts a new ImGui frame. Should be called before any ImGui calls each frame.
    /// </summary>
    static void BeginLayout(float deltaTime) => impl.BeginLayoutImpl(deltaTime);
    internal void BeginLayoutImpl(float deltaTime);

    /// <summary>
    /// Ends the ImGui frame and finalizes draw data.
    /// </summary>
    static void EndLayout() => impl.EndLayoutImpl();
    internal void EndLayoutImpl();
    
    /// <summary>
    /// Gets or Binds a texture for use by ImGui and returns a texture ID handle.
    /// </summary>
    /// <param name="texture">The texture to bind.</param>
    /// <returns>An IntPtr handle used by ImGui to reference this texture.</returns>
    static IntPtr GetOrBindTexture(ITexture texture) => impl.GetOrBindTextureImpl(texture);
    internal IntPtr GetOrBindTextureImpl(ITexture texture);
    
    /// <summary>
    /// Unbinds a previously bound texture from ImGui.
    /// </summary>
    /// <param name="texture">The texture to unbind.</param>
    static void UnbindTexture(ITexture texture) => impl.UnbindTextureImpl(texture);
    internal void UnbindTextureImpl(ITexture texture);
    
    /// <summary>
    /// Push a specific font style.
    /// </summary>
    static void UseFont(ImGuiFontStyle style) => impl.UseFontImpl(style);
    internal void UseFontImpl(ImGuiFontStyle style);
    
    /// <summary>
    /// Zoom in or out based on the given zoom rate.
    /// </summary>
    static void Zoom(float zoomRate) => impl.ZoomImpl(zoomRate);
    internal void ZoomImpl(float zoomRate);
    
    /// <summary>
    /// Gets the pointer to the main ImGui context.
    /// </summary>
    static IntPtr mainMainContextPtr => impl.mainMainContextPtrImpl;
    internal IntPtr mainMainContextPtrImpl { get; }
    
    /// <summary>
    /// Gets the pointer to the virtual ImGui context.
    /// </summary>
    static IntPtr virtualContextPtr => impl.virtualContextPtrImpl;
    internal IntPtr virtualContextPtrImpl { get; }

    /// <summary>
    /// Dispose the implementation of ImGui.
    /// </summary>
    static void DisposeImpl()
    {
        impl.Dispose();
        impl = new ImGuiNoOp();
    }
}