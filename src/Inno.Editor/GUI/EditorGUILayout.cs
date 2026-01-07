using System;
using System.Collections.Generic;
using ImGuiNET;
using Inno.Core.Math;
using Inno.Editor.Core;
using Inno.Platform.ImGui;

namespace Inno.Editor.GUI;

/// <summary>
/// EditorLayout wrapper based on IImGuiContext,
/// supports flexible layouts and common UI widgets
/// </summary>
public static class EditorGUILayout
{
    public enum LayoutAlign { Front, Center, Back }
    [Flags] public enum FontStyle { None, Bold, Italic }

    private static readonly Stack<FontStyle> FONTSTYLE_STACK = new();
    private static readonly Stack<int> SCOPE_STACK = new();
    private static readonly Stack<LayoutAlign> ALIGN_STACK = new();
    private static readonly Stack<bool> COLUMN_DIRTY_STACK = new();

    private static readonly Dictionary<int, int> COLUMN_COUNT_MAP = new();
    private static readonly Dictionary<int, float> COLUMN_TOTAL_WEIGHT_MAP = new();
    private static readonly Dictionary<int, List<float>> COLUMN_WEIGHT_MAP = new();

    private static int m_autoID = 0;
    private static int m_autoMeasureID = 0;
    private static int m_columnDepth = 0;
    private static bool m_frameBegin = false;

    #region Lifecycles

    /// <summary>
    /// Reset auto ID.
    /// </summary>
    public static void BeginFrame()
    {
        if (m_frameBegin)
            throw new InvalidOperationException("BeginFrame() can only be called once.");

        m_autoID = 0;
        m_autoMeasureID = 0;
        m_frameBegin = true;
    }

    /// <summary>
    /// Check the end condition.
    /// </summary>
    public static void EndFrame()
    {
        if (ALIGN_STACK.Count != 0 || SCOPE_STACK.Count != 0 || !m_frameBegin)
            throw new InvalidOperationException("EndFrame() is called improperly.");

        m_frameBegin = false;
    }

    /// <summary>
    /// Begin a scope for the following GUI render.
    /// </summary>
    public static void BeginScope(int id)
    {
        ImGui.PushID(id);
        SCOPE_STACK.Push(id);
    }

    /// <summary>
    /// End the current GUI scope.
    /// </summary>
    public static void EndScope()
    {
        ImGui.PopID();
        SCOPE_STACK.Pop();
    }

    #endregion

    #region Layouts

    /// <summary>
    /// Begins a column layout.
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
            var columnCount = COLUMN_COUNT_MAP[m_columnDepth];
            ImGui.BeginTable($"EditorLayout##{m_columnDepth}", columnCount, flags);

            for (var i = 0; i < columnCount; i++)
            {
                ImGui.TableSetupColumn($"Column {i}", ImGuiTableColumnFlags.None, COLUMN_WEIGHT_MAP[m_columnDepth][i]);
            }

            ImGui.TableNextRow();
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
            var totalWeight = COLUMN_TOTAL_WEIGHT_MAP[m_columnDepth];
            if (totalWeight != 0)
            {
                for (var i = 0; i < COLUMN_COUNT_MAP[m_columnDepth]; i++)
                    COLUMN_WEIGHT_MAP[m_columnDepth][i] /= totalWeight;
            }
        }

        m_columnDepth--;
    }

    /// <summary>
    /// Split columns in the current column layout.
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
    /// Inserts vertical spacing of given height (default 8px)
    /// </summary>
    public static void Space(float pixels = 8f) => ImGui.Dummy(new Vector2(1, pixels));

    /// <summary>
    /// Inserts horizontal indentation of given width (default 8px)
    /// </summary>
    public static void Indent(float pixels = 8f)
    {
        ImGui.Dummy(new Vector2(pixels, 1));
        ImGui.SameLine();
    }

    /// <summary>
    /// Begin a specific font style.
    /// </summary>
    public static void BeginFont(FontStyle style)
    {
        FONTSTYLE_STACK.Push(style);

        FontStyle result = FontStyle.None;
        foreach (var s in FONTSTYLE_STACK) result |= s;

        if (result == FontStyle.Bold) IImGui.UseFont(ImGuiFontStyle.Bold);
        else if (result == FontStyle.Italic) IImGui.UseFont(ImGuiFontStyle.Italic);
        else if (result == (FontStyle.Bold | FontStyle.Italic)) IImGui.UseFont(ImGuiFontStyle.BoldItalic);
        else IImGui.UseFont(ImGuiFontStyle.Regular);
    }

    /// <summary>
    /// End and pop the current font style.
    /// </summary>
    public static void EndFont()
    {
        FONTSTYLE_STACK.Pop();

        FontStyle result = FontStyle.None;
        foreach (var s in FONTSTYLE_STACK) result |= s;

        if (result == FontStyle.Bold) IImGui.UseFont(ImGuiFontStyle.Bold);
        else if (result == FontStyle.Italic) IImGui.UseFont(ImGuiFontStyle.Italic);
        else if (result == (FontStyle.Bold | FontStyle.Italic)) IImGui.UseFont(ImGuiFontStyle.BoldItalic);
        else IImGui.UseFont(ImGuiFontStyle.Regular);
    }

    /// <summary>
    /// Begin a new layout with specified type and alignment.
    /// </summary>
    public static void BeginAlignment(LayoutAlign align)
    {
        ALIGN_STACK.Push(align);
        ImGui.BeginGroup();
    }

    /// <summary>
    /// End the current alignment layout.
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

        // Draw background like a button (so it looks identical)
        var dl = ImGui.GetWindowDrawList();
        Vector2 p0 = ImGui.GetCursorScreenPos();
        var p1 = new Vector2(p0.x + tagSize.x, p0.y + tagSize.y);

        // Hover/active shading
        ImGui.InvisibleButton($"##tag_{axis}", tagSize);
        var hovered = ImGui.IsItemHovered();
        var held = ImGui.IsItemActive();

        // Background
        Vector4 bg = new Vector4(tagColor.r, tagColor.g, tagColor.b, tagColor.a);
        if (held)
        {
            bg = new Vector4(
                tagColor.r * 0.90f,
                tagColor.g * 0.90f, 
                tagColor.b * 0.90f,
                tagColor.a);
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

        // Centered text
        Vector2 textSize = ImGui.CalcTextSize(axis);
        var textPos = new Vector2(
            p0.x + (tagSize.x - textSize.x) * 0.5f,
            p0.y + (tagSize.y - textSize.y) * 0.5f);
        dl.AddText(textPos, ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 1)), axis);

        // --- Drag effect on the tag ---
        // When held, use mouse delta to modify value
        if (held)
        {
            // Sensitivity: tune as you like
            float speed = 0.02f;

            // Shift = fine, Ctrl = coarse
            var io = ImGui.GetIO();
            if (io.KeyShift) speed *= 0.2f;
            if (io.KeyCtrl)  speed *= 5.0f;

            float delta = io.MouseDelta.X * speed;
            if (delta != 0f)
            {
                value += delta;
                changed = true;
            }

            // change cursor while dragging
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);
        }

        // --- Value input (still edits the same value) ---
        ImGui.SameLine(0f, gap);
        ImGui.SetNextItemWidth(fieldW);
        changed |= ImGui.InputFloat($"##{axis}", ref value);

        return changed;
    }

    #endregion

    #region Widgets

    /// <summary>
    /// Render a text label
    /// </summary>
    public static void Label(string text, bool enabled = true)
    {
        float width = MeasureWidth(() => ImGui.Text(text));
        SetAlignedCursorPosX(width);

        using (new DrawScope(enabled))
        {
            float textHeight = ImGui.GetTextLineHeightWithSpacing();
            float frameHeight = ImGui.GetFrameHeight();
            float verticalOffset = (frameHeight - textHeight) * 0.5f;
            Vector2 cursorPos = ImGui.GetCursorPos();
            ImGui.SetCursorPosY(cursorPos.y + verticalOffset);
            ImGui.Text(text);
        }
    }

    /// <summary>
    /// Render a button; returns true if clicked
    /// </summary>
    public static bool Button(string label, bool enabled = true)
    {
        float width = MeasureWidth(() => ImGui.Button(label));
        SetAlignedCursorPosX(width);
        using (new DrawScope(enabled)) { return ImGui.Button(label); }
    }

    /// <summary>
    /// Render and edit an integer field.
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
    /// Render and edit a float field.
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
    /// Render and edit a Vector2 field.
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

            changed |= DrawAxisDrag(
                "X", 
                ref x,
                ImGui.GetColumnWidth(),
                new Color(0.75f, 0.20f, 0.20f));
            
            SplitColumns();

            changed |= DrawAxisDrag(
                "Y", 
                ref y, 
                ImGui.GetColumnWidth(),
                new Color(0.20f, 0.65f, 0.25f));
            
            EndColumns();

            if (changed) value = new Vector2(x, y);

            EndPropertyRow();
        }

        return changed;
    }

    /// <summary>
    /// Render and edit a Vector3 field.
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

            changed |= DrawAxisDrag(
                "X", 
                ref x,
                ImGui.GetColumnWidth(),
                new Color(0.75f, 0.20f, 0.20f));
            
            SplitColumns();

            changed |= DrawAxisDrag(
                "Y", 
                ref y, 
                ImGui.GetColumnWidth(),
                new Color(0.20f, 0.65f, 0.25f));
            
            SplitColumns();

            changed |= DrawAxisDrag(
                "Z", 
                ref z, 
                ImGui.GetColumnWidth(),
                new Color(0.25f, 0.35f, 0.80f));
            
            EndColumns();

            if (changed) value = new Vector3(x, y, z);

            EndPropertyRow();
        }

        return changed;
    }

    /// <summary>
    /// Render and edit a Quaternion field.
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

            changed |= DrawAxisDrag(
                "X", 
                ref x,
                ImGui.GetColumnWidth(),
                new Color(0.75f, 0.20f, 0.20f));
            
            SplitColumns();

            changed |= DrawAxisDrag(
                "Y", 
                ref y, 
                ImGui.GetColumnWidth(),
                new Color(0.20f, 0.65f, 0.25f));
            
            SplitColumns();

            changed |= DrawAxisDrag(
                "Z", 
                ref z, 
                ImGui.GetColumnWidth(),
                new Color(0.25f, 0.35f, 0.80f));
            
            SplitColumns();

            changed |= DrawAxisDrag(
                "W", 
                ref w, 
                ImGui.GetColumnWidth(),
                new Color(0.55f, 0.55f, 0.55f));
            
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
    /// Render and edit a text (string) field.
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
    /// Render and edit a boolean checkbox.
    /// </summary>
    public static bool Checkbox(string label, ref bool value, bool enabled = true)
    {
        using (new DrawScope(enabled))
        {
            BeginPropertyRow(label);
            // Checkbox doesn't really use width; keep it at start of control column
            bool result = ImGui.Checkbox("##value", ref value);
            EndPropertyRow();
            return result;
        }
    }

    /// <summary>
    /// Render and edit a Color field.
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
    /// Render a combo box for selecting objects.
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
    /// Render a popup menu for selecting items.
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
    /// Render a Collapsable Header with a label and an action to call when closed.
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

    #endregion
}
