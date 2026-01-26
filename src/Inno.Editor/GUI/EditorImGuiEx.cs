using System;
using System.Collections.Generic;
using Inno.Core.Math;

using ImGuiNET;
using Inno.ImGui;
using ImGuiNet = ImGuiNET.ImGui;


namespace Inno.Editor.GUI;

public static class EditorImGuiEx
{
    // UnderLine
    public static void UnderlineLastItem(float thickness = 1f, float yOffset = -1f)
    {
        var min = ImGuiNet.GetItemRectMin();
        var max = ImGuiNet.GetItemRectMax();

        float y = max.Y + yOffset;
        var col = ImGuiNet.GetColorU32(ImGuiCol.Text);

        ImGuiNet.GetWindowDrawList().AddLine(
            new Vector2(min.X, y),
            new Vector2(max.X, y),
            col,
            thickness
        );
    }
    
    // Icon
    public static void DrawIconAndText(string iconText, string text, float iconGap = 1.5f)
    {
        float iconCellWidth = ImGuiNet.GetFontSize() + iconGap;
        ImGuiNet.BeginGroup();

        // Row info
        float rowHeight = ImGuiNet.GetFrameHeight();
        Vector2 rowStart = ImGuiNet.GetCursorScreenPos();
        var currentFont = ImGuiHost.GetCurrentFont();

        // icon
        ImGuiHost.UseFont(ImGuiFontStyle.Icon, currentFont.size);
        Vector2 iconSize = ImGuiNet.CalcTextSize(iconText);
        float iconYOffset = (rowHeight - iconSize.y) * 0.5f - ImGuiNet.GetStyle().FramePadding.Y;
        ImGuiNet.SetCursorScreenPos(new Vector2(rowStart.x, rowStart.y + iconYOffset));
        ImGuiNet.TextUnformatted(iconText);

        // Text
        ImGuiHost.UseFont(currentFont);
        ImGuiNet.SetCursorScreenPos(new Vector2(rowStart.x + iconCellWidth, rowStart.y));
        ImGuiNet.TextUnformatted(text);
        
        ImGuiNet.EndGroup();
    }
    
    // Gizmos Overlay
    public static void DrawLine(Vector2 p1, Vector2 p2, Color color, float thickness = 1f)
    {
        var drawList = ImGuiNet.GetWindowDrawList();
        drawList.AddLine(new Vector2(p1.x, p1.y), new Vector2(p2.x, p2.y), color.ToUInt32ARGB(), thickness);
    }

    public static void DrawText(Vector2 pos, string text, Color color)
    {
        var drawList = ImGuiNet.GetWindowDrawList();
        drawList.AddText(new Vector2(pos.x, pos.y), color.ToUInt32ARGB(), text);
    }
    
    // Invisible
    private static bool m_inInvisible = false;
    private static Vector2 m_invisibleSizeCache = Vector2.ZERO;
    
    public static void BeginInvisible()
    {
        if (m_inInvisible) throw new InvalidOperationException("Cannot nest invisible groups.");
        m_inInvisible = true;

        System.Numerics.Vector2 currentAvailSize = ImGuiNet.GetContentRegionAvail();
        
        ImGuiNet.SetCurrentContext(ImGuiHost.virtualContextPtr);
        ImGuiNet.PushID("INVISIBLE_ID");
        ImGuiNet.BeginChild("INVISIBLE_GROUP", currentAvailSize);
        ImGuiNet.BeginGroup();
    }
    public static void EndInvisible()
    {
        ImGuiNet.EndGroup();
        m_invisibleSizeCache = new Vector2(ImGuiNet.GetItemRectSize().X, ImGuiNet.GetItemRectSize().Y);
        ImGuiNet.EndChild();
        ImGuiNet.PopID();
        ImGuiNet.SetCurrentContext(ImGuiHost.mainMainContextPtr);
        
        m_inInvisible = false;
    }
    public static Vector2 GetInvisibleItemRectSize() => m_invisibleSizeCache;
    
    // Drag & Drop Payload
    private static int m_nextPayloadId = 1;
    private static readonly Dictionary<int, object> PAYLOAD_OBJECTS = new();

    public static unsafe void SetDragPayload<T>(string type, T data) where T : unmanaged
    {
        byte* buf = stackalloc byte[sizeof(T)];
        *(T*)buf = data;
        ImGuiNet.SetDragDropPayload(type, (IntPtr)buf, (uint)sizeof(T));
    }

    public static unsafe T? AcceptDragPayload<T>(string type) where T : unmanaged
    {
        var payload = ImGuiNet.AcceptDragDropPayload(type);
        if (payload.NativePtr == null || payload.Data == IntPtr.Zero || payload.DataSize <= 0)
            return null;

        if ((uint)payload.DataSize < (uint)sizeof(T))
            return null;

        return *(T*)payload.Data.ToPointer();
    }

    public static void SetDragPayloadObject(string type, object obj)
    {
        int id = m_nextPayloadId++;
        PAYLOAD_OBJECTS[id] = obj;
        SetDragPayload(type, id);
    }

    public static T? AcceptDragPayloadObject<T>(string type) where T : class
    {
        var pid = AcceptDragPayload<int>(type);
        return pid.HasValue && PAYLOAD_OBJECTS.TryGetValue(pid.Value, out var obj)
            ? obj as T
            : null;
    }

    public static void ClearDragPayloadCache()
    {
        if (PAYLOAD_OBJECTS.Count == 0) return;
        unsafe
        {
            if (ImGuiNet.GetDragDropPayload().NativePtr == null)
            {
                PAYLOAD_OBJECTS.Clear();
            }
        }
    }
}