namespace Inno.Editor.ImGui;

public readonly struct ImGuiAlias(ImGuiFontStyle style, float size)
{
    public readonly ImGuiFontStyle style = style;
    public readonly float size = size;
}