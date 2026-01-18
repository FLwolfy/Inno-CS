using System;
using System.Collections.Generic;

using ImGuiNET;

using Inno.Core.Math;
using Inno.Platform.ImGui;

namespace Inno.Editor.GUI;

/// <summary>
/// Minimal editor layout helpers built on top of ImGui.
/// </summary>
public static class EditorGUILayout
{
    public enum LayoutAlign { Front, Center, Back }
    [Flags] public enum FontStyle { None, Bold, Italic }

    private static readonly Stack<FontStyle> FONT_STYLE_STACK = new();
    private static readonly Stack<int> SCOPE_STACK = new();
    private static readonly Stack<LayoutAlign> ALIGN_STACK = new();
    private static readonly Stack<bool> COLUMN_DIRTY_STACK = new();

    private static readonly Dictionary<int, int> COLUMN_COUNT_MAP = new();
    private static readonly Dictionary<int, float> COLUMN_TOTAL_WEIGHT_MAP = new();
    private static readonly Dictionary<int, List<float>> COLUMN_WEIGHT_MAP = new();

    private static int m_columnDepth = 0;
    private static float m_nextIndentWidth = 0;
    private static bool m_frameBegin = false;

    #region Lifecycles

    /// <summary>
    /// Begins a UI frame for EditorGUILayout validation.
    /// </summary>
    public static void BeginFrame()
    {
        if (m_frameBegin) throw new InvalidOperationException("BeginFrame() can only be called once.");
        m_frameBegin = true;
    }

    /// <summary>
    /// Ends a UI frame and validates stack state.
    /// </summary>
    public static void EndFrame()
    {
        if (ALIGN_STACK.Count != 0 || SCOPE_STACK.Count != 0 || m_nextIndentWidth != 0 || !m_frameBegin)
            throw new InvalidOperationException("EndFrame() is called improperly.");

        m_frameBegin = false;
    }

    /// <summary>
    /// Begins an ImGui ID scope.
    /// </summary>
    public static void BeginScope(int id)
    {
        ImGui.PushID(id);
        SCOPE_STACK.Push(id);
    }

    /// <summary>
    /// Ends the current ImGui ID scope.
    /// </summary>
    public static void EndScope()
    {
        ImGui.PopID();
        SCOPE_STACK.Pop();
    }

    #endregion

    #region Layouts

    /// <summary>
    /// Begins a weighted column layout.
    /// </summary>
    public static void BeginColumns(float firstColumnWeight = 1.0f, bool bordered = false)
    {
        var flags = ImGuiTableFlags.SizingStretchProp;

        if (bordered)
            flags |= ImGuiTableFlags.BordersInner | ImGuiTableFlags.BordersOuter;

        m_columnDepth++;
        bool dirty = !COLUMN_COUNT_MAP.ContainsKey(m_columnDepth);
        COLUMN_DIRTY_STACK.Push(dirty);

        if (!dirty)
        {
            int columnCount = COLUMN_COUNT_MAP[m_columnDepth];
            ImGui.BeginTable($"EditorLayout##{m_columnDepth}", columnCount, flags);

            for (int i = 0; i < columnCount; i++)
            {
                ImGui.TableSetupColumn($"Column {i}", ImGuiTableColumnFlags.None, COLUMN_WEIGHT_MAP[m_columnDepth][i]);
            }

            float rowH = ImGui.GetFrameHeight();
            ImGui.TableNextRow(ImGuiTableRowFlags.None, rowH);
            ImGui.TableSetColumnIndex(0);
        }
        else
        {
            COLUMN_COUNT_MAP[m_columnDepth] = 1;
            COLUMN_TOTAL_WEIGHT_MAP[m_columnDepth] = firstColumnWeight;
            COLUMN_WEIGHT_MAP[m_columnDepth] = new List<float> { firstColumnWeight };
        }
    }

    /// <summary>
    /// Ends the current column layout.
    /// </summary>
    public static void EndColumns()
    {
        bool dirty = COLUMN_DIRTY_STACK.Pop();

        if (!dirty)
        {
            ImGui.EndTable();
        }
        else
        {
            float totalWeight = COLUMN_TOTAL_WEIGHT_MAP[m_columnDepth];
            if (totalWeight != 0)
            {
                for (int i = 0; i < COLUMN_COUNT_MAP[m_columnDepth]; i++)
                    COLUMN_WEIGHT_MAP[m_columnDepth][i] /= totalWeight;
            }
        }

        m_columnDepth--;
    }

    /// <summary>
    /// Adds the next column in the current layout.
    /// </summary>
    public static void SplitColumns(float nextColumnWeight = 1.0f)
    {
        if (COLUMN_DIRTY_STACK.Count == 0)
            throw new InvalidOperationException("SplitColumns() called without BeginColumns().");

        if (!COLUMN_DIRTY_STACK.Peek())
        {
            ImGui.TableNextColumn();
        }
        else
        {
            COLUMN_COUNT_MAP[m_columnDepth]++;
            COLUMN_TOTAL_WEIGHT_MAP[m_columnDepth] += nextColumnWeight;
            COLUMN_WEIGHT_MAP[m_columnDepth].Add(nextColumnWeight);
        }
    }

    /// <summary>
    /// Inserts vertical spacing.
    /// </summary>
    public static void Space(float pixels = 8f) => ImGui.Dummy(new Vector2(1, pixels));

    /// <summary>
    /// Sets a one-shot indentation for the next property row.
    /// </summary>
    public static void Indent(float pixels = 8f)
    {
        m_nextIndentWidth = pixels;
    }

    /// <summary>
    /// Pushes a font style for subsequent widgets.
    /// </summary>
    public static void BeginFont(FontStyle style)
    {
        FONT_STYLE_STACK.Push(style);

        FontStyle result = FontStyle.None;
        foreach (var s in FONT_STYLE_STACK) result |= s;

        if (result == FontStyle.Bold) IImGui.UseFont(ImGuiFontStyle.Bold);
        else if (result == FontStyle.Italic) IImGui.UseFont(ImGuiFontStyle.Italic);
        else if (result == (FontStyle.Bold | FontStyle.Italic)) IImGui.UseFont(ImGuiFontStyle.BoldItalic);
        else IImGui.UseFont(ImGuiFontStyle.Regular);
    }

    /// <summary>
    /// Pops the current font style.
    /// </summary>
    public static void EndFont()
    {
        FONT_STYLE_STACK.Pop();

        FontStyle result = FontStyle.None;
        foreach (var s in FONT_STYLE_STACK) result |= s;

        if (result == FontStyle.Bold) IImGui.UseFont(ImGuiFontStyle.Bold);
        else if (result == FontStyle.Italic) IImGui.UseFont(ImGuiFontStyle.Italic);
        else if (result == (FontStyle.Bold | FontStyle.Italic)) IImGui.UseFont(ImGuiFontStyle.BoldItalic);
        else IImGui.UseFont(ImGuiFontStyle.Regular);
    }

    /// <summary>
    /// Begins an alignment group.
    /// </summary>
    public static void BeginAlignment(LayoutAlign align)
    {
        ALIGN_STACK.Push(align);
        ImGui.BeginGroup();
    }

    /// <summary>
    /// Ends the current alignment group.
    /// </summary>
    public static void EndAlignment()
    {
        if (ALIGN_STACK.Count == 0)
            throw new InvalidOperationException("EditorLayout.End called without matching Begin");

        ALIGN_STACK.Pop();
        ImGui.EndGroup();
    }

    private readonly struct DrawScope : IDisposable
    {
        private readonly bool m_enabled;

        public DrawScope(bool enabled)
        {
            m_enabled = enabled;
            if (!enabled) ImGui.BeginDisabled();
        }

        public void Dispose()
        {
            if (!m_enabled) ImGui.EndDisabled();
        }
    }

    private static float GetAlignedOffsetX(float itemWidth, float availWidth)
    {
        if (ALIGN_STACK.Count == 0) return 0f;

        var align = ALIGN_STACK.Peek();
        return align switch
        {
            LayoutAlign.Center => (availWidth - itemWidth) * 0.5f,
            LayoutAlign.Back => (availWidth - itemWidth),
            _ => 0f
        };
    }

    private static void SetAlignedCursorPosX(float itemWidth)
    {
        if (ALIGN_STACK.Count == 0) return;

        var align = ALIGN_STACK.Peek();
        var cursorPos = ImGui.GetCursorPos();
        var avail = ImGui.GetContentRegionAvail();

        float offsetX = align switch
        {
            LayoutAlign.Center => (avail.X - itemWidth) * 0.5f,
            LayoutAlign.Back => (avail.X - itemWidth),
            _ => 0f
        };

        ImGui.SetCursorPosX(cursorPos.X + offsetX);
    }

    private static float MeasureWidth(Action onMeasure)
    {
        EditorImGuiEx.BeginInvisible();
        ImGui.PushID("__measure__");
        onMeasure.Invoke();
        ImGui.PopID();
        EditorImGuiEx.EndInvisible();

        return EditorImGuiEx.GetInvisibleItemRectSize().x;
    }

    private static void BeginPropertyRow(string label)
    {
        ImGui.PushID(label);

        BeginColumns(2f);
        if (m_nextIndentWidth != 0)
        {
            ImGui.Dummy(new Vector2(m_nextIndentWidth, 0));
            ImGui.SameLine();
            m_nextIndentWidth = 0;
        }
        Label(label);

        SplitColumns(3f);

        if (!COLUMN_DIRTY_STACK.Peek())
            ImGui.TableSetColumnIndex(1);
    }

    private static void EndPropertyRow()
    {
        EndColumns();
        ImGui.PopID();
    }

    private static bool DrawAxisDrag(string axis, ref float value, float fieldW, Color tagColor)
    {
        bool changed = false;
        float gap = ImGui.GetStyle().ItemSpacing.X;
        float h = ImGui.GetFrameHeight();
        var tagSize = new Vector2(h, h);

        var dl = ImGui.GetWindowDrawList();
        Vector2 p0 = ImGui.GetCursorScreenPos();
        Vector2 p1 = new Vector2(p0.x + tagSize.x, p0.y + tagSize.y);

        ImGui.InvisibleButton($"##tag_{axis}", tagSize);
        bool hovered = ImGui.IsItemHovered();
        bool held = ImGui.IsItemActive();

        Vector4 bg = new Vector4(tagColor.r, tagColor.g, tagColor.b, tagColor.a);
        if (held)
        {
            bg = new Vector4(tagColor.r * 0.90f, tagColor.g * 0.90f, tagColor.b * 0.90f, tagColor.a);
        }
        else if (hovered)
        {
            bg = new Vector4(
                MathF.Min(1f, tagColor.r + 0.10f),
                MathF.Min(1f, tagColor.g + 0.10f),
                MathF.Min(1f, tagColor.b + 0.10f),
                tagColor.a);
        }

        float rounding = ImGui.GetStyle().FrameRounding;
        dl.AddRectFilled(p0, p1, ImGui.ColorConvertFloat4ToU32(bg), rounding);

        Vector2 textSize = ImGui.CalcTextSize(axis);
        Vector2 textPos = new Vector2(
            p0.x + (tagSize.x - textSize.x) * 0.5f,
            p0.y + (tagSize.y - textSize.y) * 0.5f);
        dl.AddText(textPos, ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 1)), axis);

        if (held)
        {
            float speed = 0.02f;
            var io = ImGui.GetIO();
            if (io.KeyShift) speed *= 0.2f;
            if (io.KeyCtrl) speed *= 5.0f;

            float delta = io.MouseDelta.X * speed;
            if (delta != 0f)
            {
                value += delta;
                changed = true;
            }

            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);
        }

        ImGui.SameLine(0f, gap);
        ImGui.SetNextItemWidth(fieldW);
        changed |= ImGui.InputFloat($"##{axis}", ref value);

        return changed;
    }

    #endregion

    #region Widgets

    /// <summary>
    /// Draws a frame-height label.
    /// </summary>
    public static void Label(string text, bool enabled = true)
    {
        float rowH = ImGui.GetFrameHeight();

        Vector2 p0 = ImGui.GetCursorScreenPos();
        float availW = ImGui.GetContentRegionAvail().X;

        ImGui.Dummy(new Vector2(1, rowH));

        float textW = ImGui.CalcTextSize(text).X;
        float offsetX = GetAlignedOffsetX(textW, availW);

        Vector2 pad = ImGui.GetStyle().FramePadding;
        uint col = ImGui.GetColorU32(enabled ? ImGuiCol.Text : ImGuiCol.TextDisabled);

        ImGui.GetWindowDrawList().AddText(
            new Vector2(p0.x + offsetX, p0.y + pad.y),
            col,
            text);
    }

    /// <summary>
    /// Draws a button with optional disable.
    /// </summary>
    public static bool Button(string label, bool enabled = true)
    {
        float width = MeasureWidth(() => ImGui.Button(label));
        SetAlignedCursorPosX(width);
        using (new DrawScope(enabled)) { return ImGui.Button(label); }
    }

    /// <summary>
    /// Draws an int input field.
    /// </summary>
    public static bool IntField(string label, ref int value, bool enabled = true)
    {
        using (new DrawScope(enabled))
        {
            BeginPropertyRow(label);
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            bool result = ImGui.InputInt("##value", ref value);
            EndPropertyRow();
            return result;
        }
    }

    /// <summary>
    /// Draws a float input field.
    /// </summary>
    public static bool FloatField(string label, ref float value, bool enabled = true)
    {
        using (new DrawScope(enabled))
        {
            BeginPropertyRow(label);
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            bool result = ImGui.InputFloat("##value", ref value);
            EndPropertyRow();
            return result;
        }
    }

    /// <summary>
    /// Draws a Vector2 field with per-axis drags.
    /// </summary>
    public static bool Vector2Field(string label, ref Vector2 value, bool enabled = true)
    {
        bool changed = false;

        using (new DrawScope(enabled))
        {
            BeginPropertyRow(label);

            float x = value.x;
            float y = value.y;

            BeginColumns();

            changed |= DrawAxisDrag("X", ref x, ImGui.GetColumnWidth(), new Color(0.75f, 0.20f, 0.20f));
            SplitColumns();
            changed |= DrawAxisDrag("Y", ref y, ImGui.GetColumnWidth(), new Color(0.20f, 0.65f, 0.25f));

            EndColumns();

            if (changed) value = new Vector2(x, y);

            EndPropertyRow();
        }

        return changed;
    }

    /// <summary>
    /// Draws a Vector3 field with per-axis drags.
    /// </summary>
    public static bool Vector3Field(string label, ref Vector3 value, bool enabled = true)
    {
        bool changed = false;

        using (new DrawScope(enabled))
        {
            BeginPropertyRow(label);

            float x = value.x;
            float y = value.y;
            float z = value.z;

            BeginColumns();

            changed |= DrawAxisDrag("X", ref x, ImGui.GetColumnWidth(), new Color(0.75f, 0.20f, 0.20f));
            SplitColumns();
            changed |= DrawAxisDrag("Y", ref y, ImGui.GetColumnWidth(), new Color(0.20f, 0.65f, 0.25f));
            SplitColumns();
            changed |= DrawAxisDrag("Z", ref z, ImGui.GetColumnWidth(), new Color(0.25f, 0.35f, 0.80f));

            EndColumns();

            if (changed) value = new Vector3(x, y, z);

            EndPropertyRow();
        }

        return changed;
    }
    
    /// <summary>
    /// Draws a Vector4 field with per-axis drags.
    /// </summary>
    public static bool Vector4Field(string label, ref Vector4 value, bool enabled = true)
    {
        bool changed = false;

        using (new DrawScope(enabled))
        {
            BeginPropertyRow(label);

            float x = value.x;
            float y = value.y;
            float z = value.z;
            float w = value.w;

            BeginColumns();
            
            changed |= DrawAxisDrag("X", ref x, ImGui.GetColumnWidth(), new Color(0.75f, 0.20f, 0.20f)); // Red
            SplitColumns();
            changed |= DrawAxisDrag("Z", ref z, ImGui.GetColumnWidth(), new Color(0.25f, 0.35f, 0.80f)); // Blue
            SplitColumns();
            changed |= DrawAxisDrag("Y", ref y, ImGui.GetColumnWidth(), new Color(0.20f, 0.65f, 0.25f)); // Green
            SplitColumns();
            changed |= DrawAxisDrag("W", ref w, ImGui.GetColumnWidth(), new Color(0.65f, 0.65f, 0.65f)); // Gray
            SplitColumns();
            
            EndColumns();

            if (changed) value = new Vector4(x, y, z, w);

            EndPropertyRow();
        }

        return changed;
    }

    /// <summary>
    /// Draws a Quaternion field with per-component drags.
    /// </summary>
    public static bool QuaternionField(string label, ref Quaternion value, bool enabled = true)
    {
        bool changed = false;

        using (new DrawScope(enabled))
        {
            BeginPropertyRow(label);

            float x = value.x;
            float y = value.y;
            float z = value.z;
            float w = value.w;

            BeginColumns();

            changed |= DrawAxisDrag("X", ref x, ImGui.GetColumnWidth(), new Color(0.75f, 0.20f, 0.20f));
            SplitColumns();
            changed |= DrawAxisDrag("Y", ref y, ImGui.GetColumnWidth(), new Color(0.20f, 0.65f, 0.25f));
            SplitColumns();
            changed |= DrawAxisDrag("Z", ref z, ImGui.GetColumnWidth(), new Color(0.25f, 0.35f, 0.80f));
            SplitColumns();
            changed |= DrawAxisDrag("W", ref w, ImGui.GetColumnWidth(), new Color(0.55f, 0.55f, 0.55f));

            EndColumns();

            if (changed)
            {
                value.x = x; value.y = y; value.z = z; value.w = w;
            }

            EndPropertyRow();
        }

        return changed;
    }

    /// <summary>
    /// Draws an input text field.
    /// </summary>
    public static bool TextField(string label, ref string value, uint maxLength = 256, bool enabled = true)
    {
        using (new DrawScope(enabled))
        {
            BeginPropertyRow(label);
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            bool result = ImGui.InputText("##value", ref value, maxLength);
            EndPropertyRow();
            return result;
        }
    }

    /// <summary>
    /// Draws a checkbox field.
    /// </summary>
    public static bool Checkbox(string label, ref bool value, bool enabled = true)
    {
        using (new DrawScope(enabled))
        {
            BeginPropertyRow(label);
            bool result = ImGui.Checkbox("##value", ref value);
            EndPropertyRow();
            return result;
        }
    }

    /// <summary>
    /// Draws a ColorEdit4 field.
    /// </summary>
    public static bool ColorField(string label, in Color input, out Color output, bool enabled = true)
    {
        bool result;

        using (new DrawScope(enabled))
        {
            BeginPropertyRow(label);

            var v = new System.Numerics.Vector4(input.r, input.g, input.b, input.a);
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            result = ImGui.ColorEdit4("##value", ref v);

            output = new Color(v.X, v.Y, v.Z, v.W);

            EndPropertyRow();
        }

        return result;
    }

    /// <summary>
    /// Draws a combo box field.
    /// </summary>
    public static bool Combo(string label, string[] list, ref int selectedIndex, bool enabled = true)
    {
        using (new DrawScope(enabled))
        {
            BeginPropertyRow(label);
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            bool result = ImGui.Combo("##value", ref selectedIndex, list, list.Length);
            EndPropertyRow();
            return result;
        }
    }

    /// <summary>
    /// Draws a button-driven popup menu.
    /// </summary>
    public static bool PopupMenu(string label, string emptyMsg, string[] itemNameList, out int? selectedIndex, bool enabled = true)
    {
        bool changed = false;
        selectedIndex = null;

        using (new DrawScope(enabled))
        {
            if (Button(label, enabled))
            {
                ImGui.OpenPopup(label);
            }

            if (ImGui.BeginPopup(label))
            {
                if (itemNameList.Length == 0)
                {
                    ImGui.Text(emptyMsg);
                }

                for (int i = 0; i < itemNameList.Length; i++)
                {
                    if (ImGui.MenuItem(itemNameList[i]))
                    {
                        selectedIndex = i;
                        changed = true;
                        ImGui.CloseCurrentPopup();
                    }
                }

                ImGui.EndPopup();
            }
        }

        return changed;
    }

    /// <summary>
    /// Draws a collapsing header (optional closable).
    /// </summary>
    public static bool CollapsingHeader(string label, Action? onClose = null, bool defaultOpen = true, bool enabled = true)
    {
        ImGui.SetNextItemOpen(defaultOpen, ImGuiCond.Once);

        bool visibility = true;
        bool result;

        if (onClose == null)
        {
            BeginFont(FontStyle.Bold);
            result = ImGui.CollapsingHeader(label);
            EndFont();
        }
        else
        {
            using (new DrawScope(enabled))
            {
                BeginFont(FontStyle.Bold);
                result = ImGui.CollapsingHeader(label, ref visibility);
                EndFont();
            }

            if (!visibility) onClose.Invoke();
        }

        return result;
    }

    /// <summary>
    /// Draws a custom collapsing label row.
    /// </summary>
    public static bool CollapsingLabel(string label, bool defaultOpen = true, bool enabled = true)
    {
        using (new DrawScope(enabled))
        {
            ImGui.PushID(label);

            var storage = ImGui.GetStateStorage();
            uint openId = ImGui.GetID("##open");
            bool open = storage.GetBool(openId, defaultOpen);
            float rowH = ImGui.GetFrameHeight();

            Vector2 p0 = ImGui.GetCursorScreenPos();

            var prevFont = IImGui.GetCurrentFont();
            IImGui.UseFont(ImGuiFontStyle.Bold);

            float labelW = ImGui.CalcTextSize(label).X;

            ImGui.InvisibleButton("##lbl_hit", new Vector2(labelW, rowH));
            bool lblHovered = ImGui.IsItemHovered();
            bool lblClicked = ImGui.IsItemClicked(ImGuiMouseButton.Left);

            ImGui.SetCursorScreenPos(p0);
            Label(label);

            IImGui.UseFont(prevFont);

            Vector2 triP0 = new Vector2(p0.x + labelW, p0.y);
            Vector2 triSize = new Vector2(rowH, rowH);

            ImGui.SetCursorScreenPos(triP0);
            ImGui.InvisibleButton("##tri_hit", triSize);
            bool triHovered = ImGui.IsItemHovered();
            bool triClicked = ImGui.IsItemClicked(ImGuiMouseButton.Left);

            bool hovered = lblHovered || triHovered;

            if (hovered)
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

            var dl = ImGui.GetWindowDrawList();
            float rounding = ImGui.GetStyle().FrameRounding;

            if (hovered)
            {
                uint bg = ImGui.GetColorU32(ImGuiCol.HeaderHovered);
                bg = (bg & 0x00FFFFFFu) | (50u << 24);
                dl.AddRectFilled(triP0, triP0 + triSize, bg, rounding);
            }

            uint col = ImGui.GetColorU32(ImGuiCol.Text);
            if (hovered) col = (col & 0x00FFFFFFu) | (255u << 24);

            float triSizePx = rowH * 0.14f;
            Vector2 center = new Vector2(triP0.x + rowH * 0.5f, triP0.y + rowH * 0.5f);

            if (open)
            {
                dl.AddTriangleFilled(
                    new Vector2(center.x - triSizePx, center.y - triSizePx * 0.6f),
                    new Vector2(center.x + triSizePx, center.y - triSizePx * 0.6f),
                    new Vector2(center.x,            center.y + triSizePx),
                    col
                );
            }
            else
            {
                dl.AddTriangleFilled(
                    new Vector2(center.x - triSizePx * 0.6f, center.y - triSizePx),
                    new Vector2(center.x - triSizePx * 0.6f, center.y + triSizePx),
                    new Vector2(center.x + triSizePx,        center.y),
                    col
                );
            }

            if (lblClicked || triClicked)
            {
                open = !open;
                storage.SetBool(openId, open);
            }

            ImGui.PopID();
            return open;
        }
    }

    /// <summary>
    /// Draws a GUID drop target field.
    /// </summary>
    public static bool GuidDrop(
        string label,
        string payloadType,
        ref Guid value,
        string? displayText = null,
        bool enabled = true)
    {
        bool changed = false;

        using (new DrawScope(enabled))
        {
            BeginPropertyRow(label);

            float w = ImGui.GetContentRegionAvail().X;
            float h = ImGui.GetFrameHeight();

            Vector2 p0 = ImGui.GetCursorScreenPos();
            Vector2 size = new Vector2(w, h);

            ImGui.InvisibleButton("##guid_drop", size);

            bool hovered = ImGui.IsItemHovered();
            bool active = ImGui.IsItemActive();

            var dl = ImGui.GetWindowDrawList();

            uint bgCol = ImGui.GetColorU32(
                active ? ImGuiCol.FrameBgActive :
                hovered ? ImGuiCol.FrameBgHovered :
                          ImGuiCol.FrameBg);

            uint borderCol = ImGui.GetColorU32(ImGuiCol.Border);

            float rounding = ImGui.GetStyle().FrameRounding;
            dl.AddRectFilled(p0, p0 + size, bgCol, rounding);
            dl.AddRect(p0, p0 + size, borderCol, rounding);

            if (enabled && ImGui.BeginDragDropTarget())
            {
                Guid? incoming = EditorImGuiEx.AcceptDragPayload<Guid>(payloadType);

                if (incoming.HasValue)
                {
                    value = incoming.Value;
                    changed = true;
                }

                ImGui.EndDragDropTarget();
            }

            if (enabled && ImGui.BeginPopupContextItem("##guid_drop_ctx"))
            {
                if (ImGui.MenuItem("Clear"))
                {
                    if (value != Guid.Empty)
                    {
                        value = Guid.Empty;
                        changed = true;
                    }
                }
                ImGui.EndPopup();
            }

            string text = value == Guid.Empty ? "None (Drop Guid Here)" : (displayText ?? value.ToString());

            Vector2 textSize = ImGui.CalcTextSize(text);
            Vector2 pad = ImGui.GetStyle().FramePadding;

            float textX = p0.x + pad.x;
            float textY = p0.y + (h - textSize.y) * 0.5f;

            uint textCol = ImGui.GetColorU32(ImGuiCol.Text);
            dl.AddText(new Vector2(textX, textY), textCol, text);

            EndPropertyRow();
        }

        return changed;
    }

    #endregion
}
