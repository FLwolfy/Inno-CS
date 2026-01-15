using System;
using ImGuiNET;
using Inno.Core.Math;
using Inno.Platform.ImGui;

namespace Inno.Editor.GUI;

public static class EditorImGuiEx
{
    private static bool m_inInvisible = false;
    private static Vector2 m_invisibleSizeCache = Vector2.ZERO;
    
    // UnderLine
    public static void UnderlineLastItem(float thickness = 1f, float yOffset = -1f)
    {
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();

        float y = max.Y + yOffset;
        var col = ImGui.GetColorU32(ImGuiCol.Text);

        ImGui.GetWindowDrawList().AddLine(
            new Vector2(min.X, y),
            new Vector2(max.X, y),
            col,
            thickness
        );
    }
    
    // Icon
    public static void DrawIconAndText(string iconText, string text, float iconGap = 1.5f)
    {
        float iconCellWidth = ImGui.GetFontSize() + iconGap;
        ImGui.BeginGroup();

        // Row info
        float rowHeight = ImGui.GetFrameHeight();
        Vector2 rowStart = ImGui.GetCursorScreenPos();
        var currentFont = IImGui.GetCurrentFont();
        var textFont = currentFont.Item1;
        var fontSize = currentFont.Item2;

        // icon
        IImGui.UseFont(ImGuiFontStyle.Font, fontSize);
        Vector2 iconSize = ImGui.CalcTextSize(iconText);
        float iconYOffset = (rowHeight - iconSize.y) * 0.5f - ImGui.GetStyle().FramePadding.Y;
        ImGui.SetCursorScreenPos(new Vector2(rowStart.x, rowStart.y + iconYOffset));
        ImGui.TextUnformatted(iconText);

        // Text
        IImGui.UseFont(textFont, fontSize);
        ImGui.SetCursorScreenPos(new Vector2(rowStart.x + iconCellWidth, rowStart.y));
        ImGui.TextUnformatted(text);
        
        ImGui.EndGroup();
    }
    
    // Gizmos Overlay
    public static void DrawLine(Vector2 p1, Vector2 p2, Color color, float thickness = 1f)
    {
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddLine(new Vector2(p1.x, p1.y), new Vector2(p2.x, p2.y), color.ToUInt32ARGB(), thickness);
    }

    public static void DrawText(Vector2 pos, string text, Color color)
    {
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddText(new Vector2(pos.x, pos.y), color.ToUInt32ARGB(), text);
    }
    
    // Invisible
    public static void BeginInvisible()
    {
        if (m_inInvisible) throw new InvalidOperationException("Cannot nest invisible groups.");
        m_inInvisible = true;

        System.Numerics.Vector2 currentAvailSize = ImGui.GetContentRegionAvail();
        
        ImGui.SetCurrentContext(IImGui.virtualContextPtr);
        ImGui.PushID("INVISIBLE_ID");
        ImGui.BeginChild("INVISIBLE_GROUP", currentAvailSize);
        ImGui.BeginGroup();
    }
    public static void EndInvisible()
    {
        ImGui.EndGroup();
        m_invisibleSizeCache = new Vector2(ImGui.GetItemRectSize().X, ImGui.GetItemRectSize().Y);
        ImGui.EndChild();
        ImGui.PopID();
        ImGui.SetCurrentContext(IImGui.mainMainContextPtr);
        
        m_inInvisible = false;
    }
    public static Vector2 GetInvisibleItemRectSize() => m_invisibleSizeCache;
    
    // Payload
    public static unsafe void SetDragDropPayload<T>(string type, T data) where T : unmanaged
    {
        T* ptr = &data;
        ImGui.SetDragDropPayload(type, (IntPtr)ptr, (uint)sizeof(T));
    }
    public static unsafe T? AcceptDragDropPayload<T>(string type) where T : unmanaged
    {
        var payload = ImGui.AcceptDragDropPayload(type);
        if (payload.NativePtr == null || payload.Data == IntPtr.Zero) { return null; }
        return *(T*)payload.Data.ToPointer();
    }
}