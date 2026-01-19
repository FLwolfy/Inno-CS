using System;
using Inno.Platform.Graphics;

namespace Inno.Platform.ImGui.Bridge;

/// <summary>
/// A no-operation implementation of IImGui for builds that do not use ImGui.
/// </summary>
internal class ImGuiNoOp : IImGui
{
    public void BeginLayoutImpl(float deltaTime)
    {
        // Do nothing
    }

    public void EndLayoutImpl()
    {
        // Do nothing
    }

    public IntPtr GetOrBindTextureImpl(ITexture texture)
    {
        // Return a dummy handle
        return IntPtr.Zero;
    }

    public void UnbindTextureImpl(ITexture texture)
    {
        // Do nothing
    }
    
    public void UseFontImpl(ImGuiFontStyle style, float? size)
    {
        // Do nothing
    }

    public ImGuiAlias GetCurrentFontImpl()
    {
        return default;
    }
    
    public void SetStorageDataImpl(string key, object? value)
    {
        // Do nothing
    }

    public T? GetStorageDataImpl<T>(string key, T? defaultValue = default)
    {
        return default;
    }

    public IntPtr mainMainContextPtrImpl => IntPtr.Zero;

    public IntPtr virtualContextPtrImpl => IntPtr.Zero;

    public void Dispose()
    {
        // Nothing to dispose
    }
}