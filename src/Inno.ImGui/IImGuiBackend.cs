using System;
using Inno.Platform.Graphics;

namespace Inno.ImGui;

/// <summary>
/// Static API for ImGui.
/// Responsible for handling frame lifecycle, rendering ImGui draw data,
/// and binding textures for use in ImGui.
/// </summary>
internal interface IImGuiBackend : IDisposable
{
    /// <summary>
    /// Starts a new ImGui frame. Should be called before any ImGui calls each frame.
    /// </summary>
    void BeginLayoutImpl(float deltaTime);

    /// <summary>
    /// Ends the ImGui frame and finalizes draw data.
    /// </summary>
    void EndLayoutImpl();
    
    /// <summary>
    /// Gets or Binds a texture for use by ImGui and returns a texture ID handle.
    /// </summary>
    /// <param name="texture">The texture to bind.</param>
    /// <returns>An IntPtr handle used by ImGui to reference this texture.</returns>
    IntPtr GetOrBindTextureImpl(ITexture texture);
    
    /// <summary>
    /// Unbinds a previously bound texture from ImGui.
    /// </summary>
    /// <param name="texture">The texture to unbind.</param>
    void UnbindTextureImpl(ITexture texture);
    
    /// <summary>
    /// Push a specific font style.
    /// </summary>
    void UseFontImpl(ImGuiFontStyle style, float? size);
    
    /// <summary>
    /// Get the font style and size in the current context.
    /// </summary>
    /// <returns></returns>
    ImGuiAlias GetCurrentFontImpl();
    
    /// <summary>
    /// Zoom in or out based on the given zoom rate.
    /// </summary>
    void ZoomImpl(float zoomRate);
    
    /// <summary>
    /// Gets the pointer to the main ImGui context.
    /// </summary>
    IntPtr mainMainContextPtrImpl { get; }
    
    /// <summary>
    /// Gets the pointer to the virtual ImGui context.
    /// </summary>
    IntPtr virtualContextPtrImpl { get; }
    
    /// <summary>
    /// Sets the UI storage data into ImGui.ini.
    /// </summary>
    void SetStorageDataImpl(string key, object? value);
    
    /// <summary>
    /// Gets the UI storage data from ImGui.ini.
    /// </summary>
    T? GetStorageDataImpl<T>(string key, T? defaultValue);
}