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
        {
            throw new InvalidOperationException("BeginFrame() can only be called once.");
        }

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
        {
            throw new InvalidOperationException("EndFrame() is called improperly.");
        }
        
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
        {
            flags |= ImGuiTableFlags.BordersInner | ImGuiTableFlags.BordersOuter;
        }
        
        m_columnDepth++;
        COLUMN_DIRTY_STACK.Push(!COLUMN_COUNT_MAP.ContainsKey(m_columnDepth));

        if (!COLUMN_DIRTY_STACK.Peek())
        {
            var columnCount = COLUMN_COUNT_MAP[m_columnDepth];
            ImGui.BeginTable("EditorLayout", columnCount, flags);

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
            COLUMN_WEIGHT_MAP[m_columnDepth] = [firstColumnWeight];
        }
        
    }

    /// <summary>
    /// Ends the current column layout.
    /// </summary>
    public static void EndColumns()
    {
        if (!COLUMN_DIRTY_STACK.Peek())
        {
            ImGui.EndTable();
        }
        else
        {
            var totalWeight = COLUMN_TOTAL_WEIGHT_MAP[m_columnDepth];
            if (totalWeight != 0)
            {
                for (var i = 0; i < COLUMN_COUNT_MAP[m_columnDepth]; i++)
                {
                
                    COLUMN_WEIGHT_MAP[m_columnDepth][i] /= totalWeight;
                }
            }
        }
        
        m_columnDepth--;
    }

    /// <summary>
    /// Split columns in the current column layout.
    /// </summary>
    public static void SplitColumns(float nextColumnWeight = 1.0f)
    {
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
    public static void Space(float pixels = 8f)
    {
        ImGui.Dummy(new Vector2(1, pixels));
    }
    
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
        if (ALIGN_STACK.Count == 0) {throw new InvalidOperationException("EditorLayout.End called without matching Begin");}
        ALIGN_STACK.Pop();
        ImGui.EndGroup();
    }
    
    private readonly struct DrawScope : IDisposable
    {
        private readonly bool m_enabled = true;

        public DrawScope(bool enabled)
        {
            ImGui.PushID(m_autoID++);
            if (!enabled)
            {
                m_enabled = enabled;
                ImGui.BeginDisabled();
            }
        }

        public void Dispose()
        {
            if (!m_enabled)
            {
                ImGui.EndDisabled();
            }
            ImGui.PopID();
        }
    }


    private static void AlignNextItem(float itemWidth)
    {
        if (ALIGN_STACK.Count == 0) return;

        var align = ALIGN_STACK.Peek();
        Vector2 cursorPos = ImGui.GetCursorPos();
        Vector2 regionAvail = ImGui.GetContentRegionAvail();

        float offsetX;
        switch (align)
        {
            case LayoutAlign.Center:
                offsetX = (regionAvail.x - itemWidth) * 0.5f;
                break;
            case LayoutAlign.Back:
                offsetX = regionAvail.x - itemWidth;
                break;
            case LayoutAlign.Front:
            default:
                offsetX = 0f;
                break;
        }

        ImGui.SetCursorPosX(cursorPos.x + offsetX);
    }

    private static float MeasureWidth(Action onMeasure)
    {
        EditorImGuiEx.BeginInvisible();
        ImGui.PushID(m_autoMeasureID++);
        onMeasure.Invoke();
        ImGui.PopID();
        EditorImGuiEx.EndInvisible();

        return EditorImGuiEx.GetInvisibleItemRectSize().x;
    }
    
    #endregion

    #region Widgets
    /// <summary>
    /// Render a text label
    /// </summary>
    public static void Label(string text, bool enabled = true)
    {
        float width = MeasureWidth(() => ImGui.Text(text));
        AlignNextItem(width);

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
        AlignNextItem(width);

        using (new DrawScope(enabled)) { return ImGui.Button(label); }
    }

    /// <summary>
    /// Render and edit an integer field
    /// </summary>
    public static bool IntField(string label, ref int value, bool enabled = true)
    {
        var dummyValue = value;
        float width = MeasureWidth(() => ImGui.InputInt(label, ref dummyValue));
        AlignNextItem(width);
        
        using (new DrawScope(enabled)) { return ImGui.InputInt(label, ref value); }
    }

    /// <summary>
    /// Render and edit a float field
    /// </summary>
    public static bool FloatField(string label, ref float value, bool enabled = true)
    {
        var dummyValue = value;
        float width = MeasureWidth(() => ImGui.InputFloat(label, ref dummyValue));
        AlignNextItem(width);
        
        using (new DrawScope(enabled)) { return ImGui.InputFloat(label, ref value); }
    }

    /// <summary>
    /// Render and edit a Vector2 field
    /// </summary>
    public static bool Vector2Field(string label, ref Vector2 value, bool enabled = true)
    {
        System.Numerics.Vector2 dummyValue = value;
        float width = MeasureWidth(() => ImGui.InputFloat2(label, ref dummyValue));
        AlignNextItem(width);

        System.Numerics.Vector2 sysValue = value;
        using (new DrawScope(enabled))
        {
            var result = ImGui.InputFloat2(label, ref sysValue);
            value = sysValue;
            return result;
        }
    }

    /// <summary>
    /// Render and edit a Vector3 field
    /// </summary>
    public static bool Vector3Field(string label, ref Vector3 value, bool enabled = true)
    {
        System.Numerics.Vector3 dummyValue = value;
        float width = MeasureWidth(() => ImGui.InputFloat3(label, ref dummyValue));
        AlignNextItem(width);
        
        System.Numerics.Vector3 sysValue = value;
        using (new DrawScope(enabled))
        {
            var result = ImGui.InputFloat3(label, ref sysValue);
            value = sysValue;
            return result;
        }
    }

    /// <summary>
    /// Render and edit a Quaternion field
    /// </summary>
    public static bool QuaternionField(string label, ref Quaternion value, bool enabled = true)
    {
        System.Numerics.Vector4 dummyValue = new(value.x, value.y, value.z, value.w);
        float width = MeasureWidth(() => ImGui.InputFloat4(label, ref dummyValue));
        AlignNextItem(width);
        
        System.Numerics.Vector4 sysValue = new(value.x, value.y, value.z, value.w);
        using (new DrawScope(enabled))
        {
            var result = ImGui.InputFloat4(label, ref sysValue);
            value.x = sysValue.X;
            value.y = sysValue.Y;
            value.z = sysValue.Z;
            value.w = sysValue.W;
            return result;
        }
    }

    /// <summary>
    /// Render and edit a text (string) field
    /// </summary>
    public static bool TextField(string label, ref string value, uint maxLength = 256, bool enabled = true)
    {
        var dummyValue = value;
        float width = MeasureWidth(() => ImGui.InputText(label, ref dummyValue, maxLength));
        AlignNextItem(width);
        
        using (new DrawScope(enabled)) { return ImGui.InputText(label, ref value, maxLength); }
    }

    /// <summary>
    /// Render and edit a boolean checkbox
    /// </summary>
    public static bool Checkbox(string label, ref bool value, bool enabled = true)
    {
        var dummyValue = value;
        float width = MeasureWidth(() => ImGui.Checkbox(label, ref dummyValue));
        AlignNextItem(width);
        
        using (new DrawScope(enabled)) { return ImGui.Checkbox(label, ref value); }
    }
    
    /// <summary>
    /// Render and edit a Color field
    /// </summary>
    public static bool ColorField(string label, in Color input, out Color output, bool enabled = true)
    {
        System.Numerics.Vector4 dummyValue = new(input.r, input.g, input.b, input.a);
        float width = MeasureWidth(() => ImGui.ColorEdit4(label, ref dummyValue));
        AlignNextItem(width);
        
        System.Numerics.Vector4 sysValue = new(input.r, input.g, input.b, input.a);
        using (new DrawScope(enabled)) 
        { 
            var result = ImGui.ColorEdit4(label, ref sysValue); 
            output = new Color(sysValue.X, sysValue.Y, sysValue.Z, sysValue.W);
            return result;
        }
    }
    
    /// <summary>
    /// Render a Collapsable Header with a label and an action to call when closed.
    /// </summary>
    public static bool CollapsingHeader(string label, Action? onClose = null, bool defaultOpen = true, bool enabled = true)
    {
        bool visibility = true;
        var openFlag = defaultOpen ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;

        bool result;
        if (onClose == null)
        {
            BeginFont(FontStyle.Bold);
            result = ImGui.CollapsingHeader(label, openFlag);
            EndFont();
        }
        else
        {
            using (new DrawScope(enabled))
            {
                BeginFont(FontStyle.Bold);
                result = ImGui.CollapsingHeader(label, ref visibility, openFlag);
                EndFont();
            }
            if (!visibility) { onClose.Invoke(); }
        }
        
        return result;
    }

    /// <summary>
    /// Render a combo box for selecting objects.
    /// </summary>
    public static bool Combo(string label, string[] list, ref int selectedIndex, bool enabled = true)
    {
        var demmySelected = selectedIndex;
        float width = MeasureWidth(() => ImGui.Combo(label, ref demmySelected, list, list.Length));
        AlignNextItem(width);
        
        using (new DrawScope(enabled)) { return ImGui.Combo(label, ref selectedIndex, list, list.Length); }
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
            AlignNextItem(MeasureWidth(() => ImGui.Button(label)));
            if (ImGui.Button(label))
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

    
    #endregion
}
